using Api.Hubs;
using Application.Services;
using Microsoft.AspNetCore.SignalR;

namespace Api.Services;

/// <summary>
/// Sends real-time notifications to connected clients by targeting the per-user
/// SignalR group ("user:{userId}") in the NotificationHub.
/// </summary>
public sealed class HubRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<NotificationHub> _hub;

    public HubRealtimeNotifier(IHubContext<NotificationHub> hub) => _hub = hub;

    public Task NotifyUserAsync(string userId, string title, string message,
        string severity = "info", CancellationToken ct = default) =>
        _hub.Clients
            .Group($"user:{userId}")
            .SendAsync("ReceiveNotification", title, message, severity, ct);
}
