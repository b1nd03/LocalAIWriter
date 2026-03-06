namespace LocalAIWriter.Core.Services;

/// <summary>
/// Controls how aggressively the engine applies corrections.
/// </summary>
public enum CorrectionAggressiveness
{
    Low = 0,
    Balanced = 1,
    High = 2
}

/// <summary>
/// Controls how strict safety validation is before applying AI output.
/// </summary>
public enum CorrectionSafetyMode
{
    Conservative = 0,
    Balanced = 1,
    Permissive = 2
}

/// <summary>
/// Unified correction options passed through pipeline and routing.
/// </summary>
public readonly record struct CorrectionOptions(
    CorrectionAggressiveness Aggressiveness,
    CorrectionSafetyMode SafetyMode)
{
    public static readonly CorrectionOptions Default =
        new(CorrectionAggressiveness.Balanced, CorrectionSafetyMode.Conservative);

    /// <summary>
    /// Creates options from persisted application setting values.
    /// </summary>
    public static CorrectionOptions FromSettings(int aggressivenessLevel, CorrectionSafetyMode safetyMode)
    {
        var level = aggressivenessLevel switch
        {
            <= 0 => CorrectionAggressiveness.Low,
            1 => CorrectionAggressiveness.Balanced,
            _ => CorrectionAggressiveness.High
        };

        return new CorrectionOptions(level, safetyMode);
    }
}

/// <summary>
/// Indicates whether the final correction was applied, rejected, or downgraded.
/// </summary>
public enum SafetyDecision
{
    Applied,
    RejectedKeepOriginal,
    FallbackRuleBased
}
