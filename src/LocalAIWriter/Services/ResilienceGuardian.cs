using Microsoft.Extensions.Logging;

namespace LocalAIWriter.Services;

/// <summary>
/// Self-healing system that monitors application health and
/// automatically recovers from failures using circuit breaker pattern.
/// </summary>
public sealed class ResilienceGuardian : IDisposable
{
    private readonly ILogger<ResilienceGuardian> _logger;
    private readonly Dictionary<string, FeatureHealth> _featureHealth = new();
    private readonly object _lock = new();
    private readonly System.Timers.Timer _recoveryTimer;

    /// <summary>Raised when degradation level changes.</summary>
    public event EventHandler<DegradationLevel>? DegradationChanged;

    /// <summary>Current degradation level.</summary>
    public DegradationLevel CurrentLevel { get; private set; } = DegradationLevel.FullFunctionality;

    public ResilienceGuardian(ILogger<ResilienceGuardian> logger)
    {
        _logger = logger;
        _recoveryTimer = new System.Timers.Timer(Core.Constants.CircuitBreakerRecoveryMs);
        _recoveryTimer.AutoReset = true;
        _recoveryTimer.Elapsed += (_, _) => AttemptRecovery();
    }

    /// <summary>Starts the health monitoring system.</summary>
    public void Start() => _recoveryTimer.Start();

    /// <summary>Records a successful operation for a feature.</summary>
    public void RecordSuccess(string featureName)
    {
        lock (_lock)
        {
            var health = GetOrCreateHealth(featureName);
            health.ConsecutiveFailures = 0;
            health.IsHealthy = true;
            health.LastSuccess = DateTime.UtcNow;
        }

        UpdateDegradationLevel();
    }

    /// <summary>Records a failure for a feature and triggers circuit breaker if threshold exceeded.</summary>
    public void RecordFailure(string featureName, Exception? ex = null)
    {
        lock (_lock)
        {
            var health = GetOrCreateHealth(featureName);
            health.ConsecutiveFailures++;
            health.TotalFailures++;
            health.LastFailure = DateTime.UtcNow;

            if (health.ConsecutiveFailures >= Core.Constants.CircuitBreakerThreshold)
            {
                health.IsHealthy = false;
                _logger.LogWarning("Circuit breaker tripped for {Feature} after {Count} failures",
                    featureName, health.ConsecutiveFailures);
            }
        }

        UpdateDegradationLevel();
    }

    /// <summary>Checks if a feature is currently healthy.</summary>
    public bool IsFeatureHealthy(string featureName)
    {
        lock (_lock)
        {
            return !_featureHealth.TryGetValue(featureName, out var health) || health.IsHealthy;
        }
    }

    /// <summary>Gets the health status of all features.</summary>
    public IReadOnlyDictionary<string, FeatureHealth> GetHealthReport()
    {
        lock (_lock)
        {
            return new Dictionary<string, FeatureHealth>(_featureHealth);
        }
    }

    public void Dispose() => _recoveryTimer.Dispose();

    private FeatureHealth GetOrCreateHealth(string name)
    {
        if (!_featureHealth.TryGetValue(name, out var health))
        {
            health = new FeatureHealth { Name = name };
            _featureHealth[name] = health;
        }
        return health;
    }

    private void AttemptRecovery()
    {
        lock (_lock)
        {
            foreach (var health in _featureHealth.Values.Where(h => !h.IsHealthy))
            {
                health.IsHealthy = true;
                health.ConsecutiveFailures = 0;
                _logger.LogInformation("Attempting recovery for {Feature}", health.Name);
            }
        }

        UpdateDegradationLevel();
    }

    private void UpdateDegradationLevel()
    {
        DegradationLevel newLevel;
        lock (_lock)
        {
            bool modelHealthy = IsFeatureHealthy("ModelInference");
            bool hookHealthy = IsFeatureHealthy("KeyboardHook");

            newLevel = (modelHealthy, hookHealthy) switch
            {
                (true, true) => DegradationLevel.FullFunctionality,
                (false, true) => DegradationLevel.RuleBasedOnly,
                (true, false) => DegradationLevel.PassiveMonitoring,
                (false, false) => DegradationLevel.SafeMode,
            };
        }

        if (newLevel != CurrentLevel)
        {
            CurrentLevel = newLevel;
            _logger.LogWarning("Degradation level changed to {Level}", newLevel);
            DegradationChanged?.Invoke(this, newLevel);
        }
    }
}

/// <summary>Health status of an individual feature.</summary>
public class FeatureHealth
{
    public string Name { get; set; } = "";
    public bool IsHealthy { get; set; } = true;
    public int ConsecutiveFailures { get; set; }
    public int TotalFailures { get; set; }
    public DateTime? LastSuccess { get; set; }
    public DateTime? LastFailure { get; set; }
}

/// <summary>Application degradation levels.</summary>
public enum DegradationLevel
{
    FullFunctionality,
    ModelDegraded,
    RuleBasedOnly,
    PassiveMonitoring,
    SafeMode
}
