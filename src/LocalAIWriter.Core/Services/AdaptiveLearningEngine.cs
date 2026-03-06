using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LocalAIWriter.Core.Services;

/// <summary>
/// Tracks user behavior to personalize corrections over time.
/// All data stored locally — never leaves device.
/// Records which suggestions users accept vs. dismiss.
/// </summary>
public sealed class AdaptiveLearningEngine
{
    private readonly ILogger<AdaptiveLearningEngine> _logger;
    private readonly object _lock = new();

    private UserLearningProfile _profile;
    private readonly string _profilePath;

    public AdaptiveLearningEngine(ILogger<AdaptiveLearningEngine> logger)
    {
        _logger = logger;
        _profilePath = Constants.LearningDbPath;
        _profile = new UserLearningProfile();
    }

    /// <summary>
    /// Loads the user's learning profile from disk.
    /// </summary>
    public void LoadProfile()
    {
        try
        {
            if (File.Exists(_profilePath))
            {
                var json = File.ReadAllText(_profilePath);
                _profile = JsonSerializer.Deserialize<UserLearningProfile>(json) ?? new UserLearningProfile();
                _logger.LogInformation("Loaded adaptive learning profile: {Interactions} interactions",
                    _profile.TotalInteractions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load learning profile — starting fresh");
            _profile = new UserLearningProfile();
        }
    }

    /// <summary>
    /// Records a user interaction (accept/dismiss/partial accept).
    /// </summary>
    public void RecordInteraction(SuggestionInteraction interaction)
    {
        lock (_lock)
        {
            _profile.TotalInteractions++;

            if (interaction.Accepted)
            {
                _profile.AcceptedCount++;
                _profile.AcceptanceByType.TryGetValue(interaction.Type, out int count);
                _profile.AcceptanceByType[interaction.Type] = count + 1;
            }
            else
            {
                _profile.DismissedCount++;
                _profile.DismissalByType.TryGetValue(interaction.Type, out int count);
                _profile.DismissalByType[interaction.Type] = count + 1;

                // If user dismisses too many of a certain type, lower confidence threshold
                // so we stop suggesting those types as aggressively
            }

            // Learn vocabulary from accepted corrections
            if (interaction.Accepted && interaction.LearnedWords is { Count: > 0 })
            {
                foreach (var word in interaction.LearnedWords)
                {
                    _profile.LearnedVocabulary.Add(word);
                }
            }
        }

        // Persist periodically (every 10 interactions)
        if (_profile.TotalInteractions % 10 == 0)
        {
            SaveProfileAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gets the confidence threshold for a given correction type,
    /// adjusted based on user acceptance patterns.
    /// </summary>
    public float GetConfidenceThreshold(CorrectionType type)
    {
        lock (_lock)
        {
            _profile.AcceptanceByType.TryGetValue(type, out int accepted);
            _profile.DismissalByType.TryGetValue(type, out int dismissed);

            int total = accepted + dismissed;
            if (total < 5) return 0.5f; // Not enough data yet

            float acceptanceRate = (float)accepted / total;

            // Higher acceptance rate → lower threshold (more suggestions)
            // Lower acceptance rate → higher threshold (fewer suggestions)
            return Math.Clamp(1.0f - acceptanceRate, 0.3f, 0.9f);
        }
    }

    /// <summary>
    /// Gets the set of vocabulary words the user has taught the system.
    /// </summary>
    public IReadOnlySet<string> GetLearnedVocabulary()
    {
        lock (_lock)
        {
            return _profile.LearnedVocabulary;
        }
    }

    /// <summary>Gets the overall acceptance rate.</summary>
    public float GetAcceptanceRate()
    {
        lock (_lock)
        {
            if (_profile.TotalInteractions == 0) return 0f;
            return (float)_profile.AcceptedCount / _profile.TotalInteractions;
        }
    }

    /// <summary>Gets the total number of interactions recorded.</summary>
    public int GetTotalInteractions() => _profile.TotalInteractions;

    /// <summary>Saves the profile to disk.</summary>
    public async Task SaveProfileAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_profilePath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json;
            lock (_lock)
            {
                json = JsonSerializer.Serialize(_profile, new JsonSerializerOptions { WriteIndented = false });
            }

            await File.WriteAllTextAsync(_profilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save learning profile");
        }
    }
}

/// <summary>A user interaction with a suggestion.</summary>
public record SuggestionInteraction(
    CorrectionType Type,
    bool Accepted,
    float Confidence,
    IReadOnlyList<string>? LearnedWords = null);

/// <summary>Serializable user learning profile.</summary>
public class UserLearningProfile
{
    public int TotalInteractions { get; set; }
    public int AcceptedCount { get; set; }
    public int DismissedCount { get; set; }
    public Dictionary<CorrectionType, int> AcceptanceByType { get; set; } = new();
    public Dictionary<CorrectionType, int> DismissalByType { get; set; } = new();
    public HashSet<string> LearnedVocabulary { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
