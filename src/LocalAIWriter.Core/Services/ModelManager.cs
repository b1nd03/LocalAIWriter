using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using LocalAIWriter.Core.Memory;
using LocalAIWriter.Core.Security;

namespace LocalAIWriter.Core.Services;

/// <summary>
/// ONNX model loader and inference engine for T5-style encoder-decoder models.
/// Supports split encoder/decoder ONNX files (HuggingFace Optimum format).
/// </summary>
public sealed class ModelManager : IDisposable
{
    private readonly ILogger<ModelManager> _logger;
    private readonly Tokenizer _tokenizer;
    private readonly InferenceMemoryManager _memoryManager;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);

    private InferenceSession? _encoderSession;
    private InferenceSession? _decoderSession;
    private InferenceSession? _singleSession; // for combined models
    private bool _isLoaded;
    private bool _isSplit;

    public bool IsLoaded => _isLoaded;
    public event EventHandler<ModelLoadedEventArgs>? ModelLoaded;

    public ModelManager(
        ILogger<ModelManager> logger,
        Tokenizer tokenizer,
        InferenceMemoryManager memoryManager)
    {
        _logger = logger;
        _tokenizer = tokenizer;
        _memoryManager = memoryManager;
    }

    /// <summary>
    /// Loads ONNX model(s). Supports both single-file and split encoder/decoder.
    /// For split: pass directory path. For single: pass .onnx file path.
    /// </summary>
    public async Task<bool> LoadModelAsync(string modelPath, CancellationToken ct = default)
    {
        if (_isLoaded) return true;

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var opts = CreateSessionOptions();

            // Check if modelPath is a directory with split encoder/decoder
            string? encoderPath = null;
            string? decoderPath = null;

            if (Directory.Exists(modelPath))
            {
                encoderPath = Path.Combine(modelPath, "encoder_model.onnx");
                decoderPath = Path.Combine(modelPath, "decoder_model.onnx");
            }
            else if (File.Exists(modelPath))
            {
                // Check sibling files for split model
                var dir = Path.GetDirectoryName(modelPath) ?? "";
                var enc = Path.Combine(dir, "encoder_model.onnx");
                var dec = Path.Combine(dir, "decoder_model.onnx");
                if (File.Exists(enc) && File.Exists(dec))
                {
                    encoderPath = enc;
                    decoderPath = dec;
                }
            }

            if (encoderPath != null && decoderPath != null &&
                File.Exists(encoderPath) && File.Exists(decoderPath))
            {
                // Split encoder/decoder model
                await Task.Run(() =>
                {
                    _encoderSession = new InferenceSession(encoderPath, opts);
                    _decoderSession = new InferenceSession(decoderPath, opts);
                }, ct);
                _isSplit = true;
                _logger.LogInformation("Split encoder/decoder model loaded");
            }
            else if (File.Exists(modelPath))
            {
                // Single combined model
                await Task.Run(() =>
                {
                    _singleSession = new InferenceSession(modelPath, opts);
                }, ct);
                _isSplit = false;
                _logger.LogInformation("Single model loaded from {Path}", modelPath);
            }
            else
            {
                _logger.LogWarning("No model files found at {Path}", modelPath);
                ModelLoaded?.Invoke(this, new ModelLoadedEventArgs(false, "Model files not found"));
                return false;
            }

            sw.Stop();
            _isLoaded = true;
            _logger.LogInformation("Model loaded in {Ms}ms", sw.ElapsedMilliseconds);
            ModelLoaded?.Invoke(this, new ModelLoadedEventArgs(true, $"Loaded in {sw.ElapsedMilliseconds}ms"));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load model");
            ModelLoaded?.Invoke(this, new ModelLoadedEventArgs(false, ex.Message));
            return false;
        }
    }

    /// <summary>
    /// Runs inference to improve/correct text.
    /// </summary>
    public async Task<string> ImproveAsync(string text, CancellationToken ct = default)
    {
        if (!_isLoaded)
        {
            _logger.LogWarning("Model not loaded");
            return text;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(Constants.InferenceTimeoutMs);

            return await Task.Run(() =>
            {
                if (_isSplit)
                    return RunSplitInference(text, timeoutCts.Token);
                else
                    return RunSingleInference(text, timeoutCts.Token);
            }, timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Inference timed out");
            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inference failed");
            return text;
        }
    }

    public void Dispose()
    {
        _encoderSession?.Dispose();
        _decoderSession?.Dispose();
        _singleSession?.Dispose();
        _inferenceLock.Dispose();
    }

    #region Private Methods

    private string RunSplitInference(string text, CancellationToken ct)
    {
        if (_encoderSession == null || _decoderSession == null) return text;

        // Tokenize
        var inputIds = _tokenizer.Encode(text, Constants.MaxInputTokens);
        var attentionMask = _tokenizer.CreateAttentionMask(inputIds);

        // Create input tensors
        var inputIdsTensor = new DenseTensor<long>(
            inputIds.Select(i => (long)i).ToArray(),
            new[] { 1, inputIds.Length });
        var attentionMaskTensor = new DenseTensor<long>(
            attentionMask, new[] { 1, attentionMask.Length });

        ct.ThrowIfCancellationRequested();

        _inferenceLock.Wait(ct);
        try
        {
            // Step 1: Run encoder
            var encoderInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
            };

            using var encoderResults = _encoderSession.Run(encoderInputs);
            var encoderHidden = encoderResults.First().AsTensor<float>();

            // Step 2: Autoregressive decoding
            var outputTokens = new List<int>();
            var decoderInputId = new long[] { Tokenizer.PadTokenId }; // Start token

            for (int step = 0; step < Constants.MaxOutputTokens; step++)
            {
                ct.ThrowIfCancellationRequested();

                var decoderInputTensor = new DenseTensor<long>(
                    decoderInputId, new[] { 1, 1 });

                var decoderInputs = new List<NamedOnnxValue>();

                // Check what the decoder expects
                var inputNames = _decoderSession.InputMetadata.Keys.ToHashSet();

                if (inputNames.Contains("input_ids"))
                    decoderInputs.Add(NamedOnnxValue.CreateFromTensor("input_ids", decoderInputTensor));
                else if (inputNames.Contains("decoder_input_ids"))
                    decoderInputs.Add(NamedOnnxValue.CreateFromTensor("decoder_input_ids", decoderInputTensor));

                if (inputNames.Contains("encoder_hidden_states"))
                    decoderInputs.Add(NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHidden));

                if (inputNames.Contains("encoder_attention_mask"))
                    decoderInputs.Add(NamedOnnxValue.CreateFromTensor("encoder_attention_mask", attentionMaskTensor));

                using var decoderResults = _decoderSession.Run(decoderInputs);
                var logits = decoderResults.First().AsTensor<float>();

                // Greedy: take argmax of last position
                var shape = logits.Dimensions;
                int vocabSize = shape[^1];
                int lastPos = shape.Length >= 3 ? shape[1] - 1 : 0;

                float maxVal = float.MinValue;
                int maxIdx = 0;
                for (int v = 0; v < vocabSize; v++)
                {
                    float val = shape.Length >= 3 ? logits[0, lastPos, v] : logits[0, v];
                    if (val > maxVal) { maxVal = val; maxIdx = v; }
                }

                if (maxIdx == Tokenizer.EosTokenId) break;

                outputTokens.Add(maxIdx);
                decoderInputId = new long[] { maxIdx };
            }

            return _tokenizer.Decode(outputTokens.ToArray());
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    private string RunSingleInference(string text, CancellationToken ct)
    {
        if (_singleSession == null) return text;

        var inputIds = _tokenizer.Encode(text, Constants.MaxInputTokens);
        var attentionMask = _tokenizer.CreateAttentionMask(inputIds);

        var inputIdsTensor = new DenseTensor<long>(
            inputIds.Select(i => (long)i).ToArray(),
            new[] { 1, inputIds.Length });
        var attentionMaskTensor = new DenseTensor<long>(
            attentionMask, new[] { 1, attentionMask.Length });

        var inputs = new List<NamedOnnxValue>();
        var inputNames = _singleSession.InputMetadata.Keys.ToArray();

        if (inputNames.Contains("input_ids"))
            inputs.Add(NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor));
        if (inputNames.Contains("attention_mask"))
            inputs.Add(NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor));
        if (inputNames.Contains("decoder_input_ids"))
        {
            var decoderIds = new DenseTensor<long>(new long[] { 0 }, new[] { 1, 1 });
            inputs.Add(NamedOnnxValue.CreateFromTensor("decoder_input_ids", decoderIds));
        }

        ct.ThrowIfCancellationRequested();

        _inferenceLock.Wait(ct);
        try
        {
            using var results = _singleSession.Run(inputs);
            var outputTensor = results.First().AsTensor<float>();

            var outputIds = new int[Constants.MaxOutputTokens];
            var shape = outputTensor.Dimensions;

            if (shape.Length >= 3)
            {
                int seqLen = shape[1];
                int vocabSize = shape[2];

                for (int t = 0; t < Math.Min(seqLen, Constants.MaxOutputTokens); t++)
                {
                    float maxVal = float.MinValue;
                    int maxIdx = 0;
                    for (int v = 0; v < vocabSize; v++)
                    {
                        float val = outputTensor[0, t, v];
                        if (val > maxVal) { maxVal = val; maxIdx = v; }
                    }
                    outputIds[t] = maxIdx;
                    if (maxIdx == Tokenizer.EosTokenId) break;
                }
            }

            return _tokenizer.Decode(outputIds);
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    private static SessionOptions CreateSessionOptions()
    {
        var opts = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            InterOpNumThreads = 2,
            IntraOpNumThreads = Math.Min(4, Environment.ProcessorCount)
        };
        opts.EnableMemoryPattern = true;
        opts.EnableCpuMemArena = true;
        return opts;
    }

    #endregion
}

/// <summary>Event args for model loading.</summary>
public class ModelLoadedEventArgs : EventArgs
{
    public bool Success { get; }
    public string Message { get; }
    public ModelLoadedEventArgs(bool success, string message)
    {
        Success = success;
        Message = message;
    }
}
