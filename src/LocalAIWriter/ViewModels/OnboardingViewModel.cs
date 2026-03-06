using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LocalAIWriter.ViewModels;

/// <summary>ViewModel for the first-launch onboarding flow.</summary>
public sealed partial class OnboardingViewModel : ObservableObject
{
    [ObservableProperty] private int _currentStep;
    [ObservableProperty] private int _totalSteps = 4;

    public event EventHandler? Completed;

    [RelayCommand]
    private void NextStep()
    {
        if (CurrentStep < TotalSteps - 1)
            CurrentStep++;
        else
            Completed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep > 0)
            CurrentStep--;
    }

    [RelayCommand]
    private void Skip()
    {
        Completed?.Invoke(this, EventArgs.Empty);
    }
}
