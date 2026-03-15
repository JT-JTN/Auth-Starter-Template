using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Api.Hubs;

/// <summary>
/// Real-time notification hub.
/// Authenticated users are added to a personal group ("user:{userId}") on connect,
/// so the server can target a specific user's connections via IRealtimeNotifier.
/// </summary>
[Authorize]
public sealed class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        // With MapInboundClaims = false, "sub" is NOT remapped to ClaimTypes.NameIdentifier,
        // so Context.UserIdentifier (which reads NameIdentifier) would always be null.
        // Read the "sub" claim directly to get the user ID.
        var userId = Context.User?.FindFirstValue("sub");
        if (userId is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");

        await base.OnConnectedAsync();
    }
}
