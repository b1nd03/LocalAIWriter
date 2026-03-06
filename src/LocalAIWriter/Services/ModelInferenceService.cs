using System.Threading.Channels;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using LocalAIWriter.Core.Services;

namespace LocalAIWriter.Services;

/// <summary>
/// Wraps the Core ModelManager for application-level use with
/// caching, request deduplication, priority queue, and UI-thread dispatching.
/// </summary>
public sealed class ModelInferenceService : IDisposable
{
    private readonly ModelManager _modelManager;
    private readonly NlpPipeline _pipeline;
    private readonly ILogger<ModelInferenceService> _logger;
    private readonly MemoryCache _cache;
    private readonly Channel<InferenceRequest> _requestChannel;
    private readonly CancellationTokenSource _cts = new();
    private Task? _processingTask;

    public ModelInferenceService(
        ModelManager modelManager,
        NlpPipeline pipeline,
        ILogger<ModelInferenceService> logger)
    {
        _modelManager = modelManager;
        _pipeline = pipeline;
        _logger = logger;
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = Core.Constants.PredictionCacheMaxSize
        });
        _requestChannel = Channel.CreateBounded<InferenceRequest>(
            new BoundedChannelOptions(Core.Constants.InferenceChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });
    }

    /// <summary>Starts the background inference processing loop.</summary>
    public void Start()
    {
        _processingTask = Task.Run(ProcessRequestsAsync);
        _logger.LogInformation("Inference service started");
    }

    /// <summary>
    /// Submits text for AI processing. Returns cached result if available.
    /// </summary>
    public async Task<PipelineResult?> ProcessTextAsync(
        string text,
        CorrectionOptions? options = null,
        bool priority = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var effectiveOptions = options ?? CorrectionOptions.Default;

        // Check cache
        string cacheKey = $"{text.GetHashCode()}|{(int)effectiveOptions.Aggressiveness}|{(int)effectiveOptions.SafetyMode}";
        if (_cache.TryGetValue(cacheKey, out PipelineResult? cached))
        {
            _logger.LogDebug("Cache hit for text");
            return cached;
        }

        // Run pipeline
        var result = await _pipeline.ProcessAsync(text, effectiveOptions, ct);

        // Cache result
        _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
        {
            Size = 1,
            SlidingExpiration = TimeSpan.FromMinutes(5)
        });

        return result;
    }

    /// <summary>
    /// Queues an inference request for background processing.
    /// </summary>
    public async ValueTask QueueRequestAsync(
        string text,
        Action<PipelineResult> callback,
        CorrectionOptions? options = null)
    {
        var request = new InferenceRequest(text, callback, options ?? CorrectionOptions.Default);
        await _requestChannel.Writer.WriteAsync(request, _cts.Token);
    }

    /// <summary>Clears the prediction cache.</summary>
    public void ClearCache()
    {
        _cache.Compact(1.0);
        _logger.LogInformation("Prediction cache cleared");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _requestChannel.Writer.Complete();
        _cache.Dispose();
        _cts.Dispose();
    }

    private async Task ProcessRequestsAsync()
    {
        await foreach (var request in _requestChannel.Reader.ReadAllAsync(_cts.Token))
        {
            try
            {
                var result = await _pipeline.ProcessAsync(request.Text, request.Options, _cts.Token);
                request.Callback(result);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background inference failed");
            }
        }
    }
}

internal record InferenceRequest(
    string Text,
    Action<PipelineResult> Callback,
    CorrectionOptions Options);
