using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalAIWriter.Core.Extensions;
using LocalAIWriter.Core.Services;
using Microsoft.Extensions.Logging;

namespace LocalAIWriter.ViewModels;

/// <summary>
/// ViewModel for the suggestion popup overlay.
/// Manages the suggestion list, user actions (accept/dismiss),
/// and adaptive learning feedback.
/// </summary>
public sealed partial class SuggestionPopupViewModel : ObservableObject
{
    private readonly Services.TextInterceptor _textInterceptor;
    private readonly AdaptiveLearningEngine _learningEngine;
    private readonly ILogger _logger;

    /// <summary>Raised when the popup is dismissed (by user or timeout).</summary>
    public event EventHandler? Dismissed;

    [ObservableProperty]
    private string _originalText = string.Empty;

    [ObservableProperty]
    private string _correctedText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<DiffSegment> _diffSegments = Array.Empty<DiffSegment>();

    [ObservableProperty]
    private float _confidence;

    [ObservableProperty]
    private string _routeLabel = string.Empty;

    [ObservableProperty]
    private long _latencyMs;

    [ObservableProperty]
    private double _popupLeft;

    [ObservableProperty]
    private double _popupTop;

    public SuggestionPopupViewModel(
        PipelineResult result,
        Point? position,
        Services.TextInterceptor textInterceptor,
        AdaptiveLearningEngine learningEngine,
        ILogger logger)
    {
        _textInterceptor = textInterceptor;
        _learningEngine = learningEngine;
        _logger = logger;

        OriginalText = result.OriginalText;
        CorrectedText = result.CorrectedText;
        DiffSegments = result.Diff;
        Confidence = result.Corrections.Count > 0
            ? result.Corrections.Average(c => c.Confidence) : 0.8f;
        RouteLabel = result.Route.ToString();
        LatencyMs = result.TotalLatencyMs;

        if (position.HasValue)
        {
            PopupLeft = position.Value.X;
            PopupTop = position.Value.Y + Core.Constants.PopupOffsetY;
        }
    }

    [RelayCommand]
    private async Task AcceptAsync()
    {
        try
        {
            await _textInterceptor.ReplaceTextAsync(CorrectedText);
            _learningEngine.RecordInteraction(new SuggestionInteraction(
                CorrectionType.Grammar, Accepted: true, Confidence));
            _logger.LogInformation("Suggestion accepted (confidence={Conf:F2})", Confidence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply correction");
        }
        finally
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void Dismiss()
    {
        _learningEngine.RecordInteraction(new SuggestionInteraction(
            CorrectionType.Grammar, Accepted: false, Confidence));
        _logger.LogDebug("Suggestion dismissed");
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
}
