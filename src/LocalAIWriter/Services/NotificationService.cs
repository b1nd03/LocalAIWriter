using Microsoft.Extensions.Logging;

namespace LocalAIWriter.Services;

/// <summary>
/// Manages system tray balloon notifications and toast messages.
/// </summary>
public sealed class NotificationService
{
    private readonly ILogger<NotificationService> _logger;

    /// <summary>Raised when a notification should be shown via the tray icon.</summary>
    public event EventHandler<NotificationEventArgs>? NotificationRequested;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    /// <summary>Shows an informational notification.</summary>
    public void ShowInfo(string title, string message)
    {
        _logger.LogInformation("Notification: {Title} - {Message}", title, message);
        NotificationRequested?.Invoke(this, new NotificationEventArgs(title, message, NotificationType.Info));
    }

    /// <summary>Shows a success notification.</summary>
    public void ShowSuccess(string title, string message)
    {
        _logger.LogInformation("Success: {Title} - {Message}", title, message);
        NotificationRequested?.Invoke(this, new NotificationEventArgs(title, message, NotificationType.Success));
    }

    /// <summary>Shows a warning notification.</summary>
    public void ShowWarning(string title, string message)
    {
        _logger.LogWarning("Warning: {Title} - {Message}", title, message);
        NotificationRequested?.Invoke(this, new NotificationEventArgs(title, message, NotificationType.Warning));
    }

    /// <summary>Shows an error notification.</summary>
    public void ShowError(string title, string message)
    {
        _logger.LogError("Error: {Title} - {Message}", title, message);
        NotificationRequested?.Invoke(this, new NotificationEventArgs(title, message, NotificationType.Error));
    }
}

public sealed class NotificationEventArgs : EventArgs
{
    public string Title { get; }
    public string Message { get; }
    public NotificationType Type { get; }

    public NotificationEventArgs(string title, string message, NotificationType type)
    {
        Title = title;
        Message = message;
        Type = type;
    }
}

public enum NotificationType { Info, Success, Warning, Error }
