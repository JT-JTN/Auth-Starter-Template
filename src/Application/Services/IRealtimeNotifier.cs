namespace Application.Services;

/// <summary>
/// Pushes real-time notifications to a specific user's connected clients.
/// The implementation (HubRealtimeNotifier) lives in the Api project and uses
/// IHubContext&lt;NotificationHub&gt;. A no-op is used in contexts where SignalR
/// is unavailable (e.g. unit tests).
/// </summary>
public interface IRealtimeNotifier
{
    /// <summary>
    /// Sends a notification to all active SignalR connections for the given user.
    /// severity: "info" | "success" | "warning" | "error"
    /// </summary>
    Task NotifyUserAsync(string userId, string title, string message,
        string severity = "info", CancellationToken ct = default);
}
