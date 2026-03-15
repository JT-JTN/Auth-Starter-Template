using Application.Services;

namespace Infrastructure.Services;

/// <summary>
/// Fallback no-op notifier used in contexts where SignalR is unavailable
/// (unit tests, background jobs running outside the API host, etc.).
/// The Api project overrides this with HubRealtimeNotifier.
/// </summary>
internal sealed class NoOpRealtimeNotifier : IRealtimeNotifier
{
    public Task NotifyUserAsync(string userId, string title, string message,
        string severity = "info", CancellationToken ct = default) => Task.CompletedTask;
}
