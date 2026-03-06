using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LocalAIWriter.Core.Services;

/// <summary>
/// Tokenizer that loads from HuggingFace tokenizer.json format.
/// Handles encoding text to token IDs and decoding back.
/// </summary>
public sealed class Tokenizer : IDisposable
{
    private readonly Dictionary<string, int> _vocabToId;
    private readonly Dictionary<int, string> _idToVocab;
    private readonly ILogger<Tokenizer> _logger;
    private bool _isLoaded;

    public bool IsLoaded => _isLoaded;
    public int VocabularySize => _vocabToId.Count;

    // T5 special tokens
    public const int PadTokenId = 0;
    public const int EosTokenId = 1;
    public const int UnkTokenId = 2;

    public Tokenizer(ILogger<Tokenizer> logger)
    {
        _logger = logger;
        _vocabToId = new Dictionary<string, int>(32000);
        _idToVocab = new Dictionary<int, string>(32000);
    }

    /// <summary>
    /// Loads vocabulary from a HuggingFace tokenizer.json file.
    /// </summary>
    public void LoadVocabulary(string modelPath)
    {
        // Try loading tokenizer.json from the same directory
        var dir = Path.GetDirectoryName(modelPath) ?? "";
        var tokenizerJsonPath = Path.Combine(dir, "tokenizer.json");

        if (File.Exists(tokenizerJsonPath))
        {
            LoadFromTokenizerJson(tokenizerJsonPath);
            return;
        }

        if (File.Exists(modelPath) && modelPath.EndsWith(".json"))
        {
            LoadFromTokenizerJson(modelPath);
            return;
        }

        _logger.LogWarning("Tokenizer file not found. Using fallback tokenizer.");
        InitializeFallbackVocabulary();
    }

    private void LoadFromTokenizerJson(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);

            // HuggingFace tokenizer.json has: model.vocab (array of [token, score] pairs)
            if (doc.RootElement.TryGetProperty("model", out var model) &&
                model.TryGetProperty("vocab", out var vocab))
            {
                _vocabToId.Clear();
                _idToVocab.Clear();

                int id = 0;
                foreach (var entry in vocab.EnumerateArray())
                {
                    if (entry.GetArrayLength() >= 1)
                    {
                        var token = entry[0].GetString() ?? "";
                        if (!_vocabToId.ContainsKey(token))
                        {
                            _vocabToId[token] = id;
                            _idToVocab[id] = token;
                        }
                        id++;
                    }
                }

                _isLoaded = true;
                _logger.LogInformation("Tokenizer loaded {Count} tokens from {Path}", _vocabToId.Count, path);
                return;
            }

            // Alternative: added_tokens or vocab as object
            if (doc.RootElement.TryGetProperty("added_tokens", out var addedTokens))
            {
                foreach (var token in addedTokens.EnumerateArray())
                {
                    if (token.TryGetProperty("content", out var content) &&
                        token.TryGetProperty("id", out var tid))
                    {
                        var tokenStr = content.GetString() ?? "";
                        var tokenId = tid.GetInt32();
                        _vocabToId[tokenStr] = tokenId;
                        _idToVocab[tokenId] = tokenStr;
                    }
                }
            }

            _isLoaded = _vocabToId.Count > 0;
            if (_isLoaded)
                _logger.LogInformation("Tokenizer loaded {Count} tokens", _vocabToId.Count);
            else
            {
                _logger.LogWarning("Could not parse tokenizer.json. Using fallback.");
                InitializeFallbackVocabulary();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tokenizer.json");
            InitializeFallbackVocabulary();
        }
    }

    /// <summary>
    /// Encodes text into token IDs using simple word/subword matching.
    /// </summary>
    public int[] Encode(string text, int maxLength = Constants.MaxInputTokens)
    {
        var tokens = new List<int>();

        if (string.IsNullOrWhiteSpace(text))
        {
            tokens.Add(EosTokenId);
            return PadToLength(tokens, maxLength);
        }

        // T5 grammar correction uses "grammar: " prefix
        var prefixed = text;

        // SentencePiece-style: replace spaces with ▁ and try matching
        var words = prefixed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool first = true;

        foreach (var word in words)
        {
            var searchWord = first ? "▁" + word : "▁" + word;
            first = false;

            // Try full word match
            if (_vocabToId.TryGetValue(searchWord, out int wordId))
            {
                tokens.Add(wordId);
                continue;
            }

            // Try without ▁ prefix
            if (_vocabToId.TryGetValue(word, out int wordId2))
            {
                tokens.Add(wordId2);
                continue;
            }

            // Try lowercase
            var lower = searchWord.ToLowerInvariant();
            if (_vocabToId.TryGetValue(lower, out int lowerId))
            {
                tokens.Add(lowerId);
                continue;
            }

            // Character-level fallback
            foreach (char c in word)
            {
                var charToken = "▁" + c;
                if (_vocabToId.TryGetValue(charToken, out int charId))
                    tokens.Add(charId);
                else if (_vocabToId.TryGetValue(c.ToString(), out int charId2))
                    tokens.Add(charId2);
                else
                    tokens.Add(UnkTokenId);
            }
        }

        tokens.Add(EosTokenId);
        return PadToLength(tokens, maxLength);
    }

    /// <summary>
    /// Decodes token IDs back to text.
    /// </summary>
    public string Decode(ReadOnlySpan<int> tokenIds)
    {
        var sb = new System.Text.StringBuilder(tokenIds.Length * 5);

        foreach (int id in tokenIds)
        {
            if (id == PadTokenId || id == EosTokenId)
                continue;

            if (_idToVocab.TryGetValue(id, out var token))
            {
                if (token.StartsWith("▁"))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(token[1..]);
                }
                else
                {
                    sb.Append(token);
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Creates an attention mask (1 for real tokens, 0 for padding).
    /// </summary>
    public long[] CreateAttentionMask(int[] tokenIds)
    {
        var mask = new long[tokenIds.Length];
        for (int i = 0; i < tokenIds.Length; i++)
            mask[i] = tokenIds[i] != PadTokenId ? 1L : 0L;
        return mask;
    }

    public void Dispose()
    {
        _vocabToId.Clear();
        _idToVocab.Clear();
    }

    #region Private Methods

    private static int[] PadToLength(List<int> tokens, int maxLength)
    {
        var result = new int[maxLength];
        for (int i = 0; i < Math.Min(tokens.Count, maxLength); i++)
            result[i] = tokens[i];
        return result;
    }

    private void InitializeFallbackVocabulary()
    {
        _vocabToId.Clear();
        _idToVocab.Clear();

        _vocabToId["<pad>"] = PadTokenId;
        _vocabToId["</s>"] = EosTokenId;
        _vocabToId["<unk>"] = UnkTokenId;
        _idToVocab[PadTokenId] = "<pad>";
        _idToVocab[EosTokenId] = "</s>";
        _idToVocab[UnkTokenId] = "<unk>";

        int id = 3;
        for (char c = 'a'; c <= 'z'; c++)
        {
            _vocabToId[c.ToString()] = id;
            _idToVocab[id] = c.ToString();
            id++;
        }
        foreach (var punct in new[] { ".", ",", "!", "?", "'", "\"", " ", "-", ":", ";" })
        {
            _vocabToId[punct] = id;
            _idToVocab[id] = punct;
            id++;
        }

        foreach (var word in CommonWords)
        {
            if (!_vocabToId.ContainsKey(word))
            {
                _vocabToId[word] = id;
                _idToVocab[id] = word;
                id++;
            }
        }

        _isLoaded = true;
        _logger.LogInformation("Fallback tokenizer initialized with {Count} tokens", _vocabToId.Count);
    }

    private static readonly string[] CommonWords =
    {
        "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
        "have", "has", "had", "do", "does", "did", "will", "would", "could",
        "should", "may", "might", "can", "shall", "must", "need",
        "i", "you", "he", "she", "it", "we", "they", "me", "him", "her",
        "us", "them", "my", "your", "his", "its", "our", "their",
        "this", "that", "these", "those", "and", "but", "or", "not", "no",
        "in", "on", "at", "to", "for", "with", "from", "by", "of",
    };

    #endregion
}
