using LocalAIWriter.Core.Services;

namespace LocalAIWriter.Models;

/// <summary>
/// Represents a single AI-generated suggestion for text correction.
/// </summary>
public sealed class Suggestion
{
    /// <summary>The suggested replacement text.</summary>
    public required string Text { get; init; }

    /// <summary>The original text being corrected.</summary>
    public required string OriginalText { get; init; }

    /// <summary>Confidence score (0.0 – 1.0).</summary>
    public float Confidence { get; init; }

    /// <summary>Category of the correction.</summary>
    public CorrectionType Category { get; init; }

    /// <summary>Human-readable explanation of why this correction was suggested.</summary>
    public string? Explanation { get; init; }

    /// <summary>Which inference route produced this suggestion.</summary>
    public InferenceRoute Route { get; init; }
}

/// <summary>
/// Represents a text correction diff for display in the UI.
/// </summary>
public sealed class CorrectionDiff
{
    /// <summary>The original text.</summary>
    public required string OriginalText { get; init; }

    /// <summary>The corrected text.</summary>
    public required string CorrectedText { get; init; }

    /// <summary>Word-level diff segments for rendering.</summary>
    public required IReadOnlyList<Core.Extensions.DiffSegment> Segments { get; init; }

    /// <summary>Overall confidence of the correction.</summary>
    public float Confidence { get; init; }

    /// <summary>Latency of the correction in milliseconds.</summary>
    public long LatencyMs { get; init; }
}

/// <summary>
/// Represents the writing context detected from the active application.
/// </summary>
public sealed class WritingContext
{
    /// <summary>The detected application type.</summary>
    public ApplicationType AppType { get; init; }

    /// <summary>The formality level to use.</summary>
    public FormalityLevel Formality { get; init; }

    /// <summary>Whether the text appears to contain code.</summary>
    public bool IsCodeMixed { get; init; }

    /// <summary>Name of the active application process.</summary>
    public string? ProcessName { get; init; }
}

/// <summary>Type of application detected.</summary>
public enum ApplicationType
{
    Unknown,
    TextEditor,
    CodeEditor,
    EmailClient,
    ChatApplication,
    DocumentEditor,
    Browser,
    Terminal
}

/// <summary>Expected formality level.</summary>
public enum FormalityLevel
{
    Casual,
    Neutral,
    Formal,
    Academic,
    Legal
}

/// <summary>
/// User profile data for adaptive learning display.
/// </summary>
public sealed class UserProfile
{
    public int TotalCorrections { get; set; }
    public int AcceptedCorrections { get; set; }
    public float AcceptanceRate { get; set; }
    public TimeSpan AverageLatency { get; set; }
    public int ModelFailures { get; set; }
    public int RuleBasedFallbacks { get; set; }
}
