using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;

namespace WebClient.Services;

/// <summary>
/// Manages the SignalR connection to the server's /hubs/notifications endpoint.
/// Starts automatically when the user is authenticated and stops on logout.
/// Server push messages are fed into <see cref="NotificationService"/> so they
/// appear in the notification bell without any polling.
/// </summary>
public sealed class NotificationHubService : IAsyncDisposable
{
    private readonly IConfiguration _config;
    private readonly TokenStorageService _tokenStorage;
    private readonly NotificationService _notifications;
    private readonly AuthenticationStateProvider _authState;

    private HubConnection? _hub;
    private bool _started;

    public NotificationHubService(
        IConfiguration config,
        TokenStorageService tokenStorage,
        NotificationService notifications,
        AuthenticationStateProvider authState)
    {
        _config = config;
        _tokenStorage = tokenStorage;
        _notifications = notifications;
        _authState = authState;

        _authState.AuthenticationStateChanged += OnAuthStateChanged;
    }

    private async void OnAuthStateChanged(Task<AuthenticationState> task)
    {
        var state = await task;
        if (state.User.Identity?.IsAuthenticated == true)
            await StartAsync();
        else
            await StopAsync();
    }

    /// <summary>
    /// Connects to the hub if the user has a valid access token.
    /// Safe to call multiple times — idempotent once connected.
    /// </summary>
    public async Task StartAsync()
    {
        if (_started) return;

        var token = await _tokenStorage.GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token)) return;

        var baseUrl = _config["Backend:BaseUrl"] ?? "https://localhost:7170";
        var hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/notifications";

        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                // SignalR passes the JWT via query string for WebSocket transport
                // because browsers cannot add custom headers to WebSocket upgrades.
                options.AccessTokenProvider = () => _tokenStorage.GetAccessTokenAsync().AsTask();
            })
            .WithAutomaticReconnect()
            .Build();

        _hub.On<string, string, string>("ReceiveNotification", (title, message, severity) =>
        {
            var sev = severity.ToLowerInvariant() switch
            {
                "success" => AppNotificationSeverity.Success,
                "warning" => AppNotificationSeverity.Warning,
                "error"   => AppNotificationSeverity.Error,
                _         => AppNotificationSeverity.Info
            };
            _notifications.Add(title, message, sev);
        });

        try
        {
            await _hub.StartAsync();
            _started = true;
        }
        catch
        {
            // Hub connection failure is non-fatal — the app works without real-time
            // notifications (bell still shows locally-added notifications).
        }
    }

    public async Task StopAsync()
    {
        if (_hub is not null)
        {
            await _hub.StopAsync();
            await _hub.DisposeAsync();
            _hub = null;
        }
        _started = false;
    }

    public async ValueTask DisposeAsync()
    {
        _authState.AuthenticationStateChanged -= OnAuthStateChanged;
        await StopAsync();
    }
}
