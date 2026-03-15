using Api.Hubs;
using Api.Services;
using Application.DTOs.Admin;
using Application.DTOs.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Moq;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

// ── Unit tests ─────────────────────────────────────────────────────────────────

/// <summary>
/// Unit tests for HubRealtimeNotifier.
/// Mocks IHubContext so no real SignalR infrastructure is needed.
/// </summary>
public class HubRealtimeNotifierTests
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task NotifyUserAsync_TargetsCorrectGroupAndMethod()
    {
        // Arrange
        var mockProxy = new Mock<IClientProxy>();
        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.Group("user:user-42")).Returns(mockProxy.Object);

        var mockHub = new Mock<IHubContext<NotificationHub>>();
        mockHub.Setup(h => h.Clients).Returns(mockClients.Object);

        var notifier = new HubRealtimeNotifier(mockHub.Object);

        // Act
        await notifier.NotifyUserAsync("user-42", "Title", "Body", "warning");

        // Assert — SendCoreAsync is the interface method; SendAsync extension packs args into it
        mockProxy.Verify(p => p.SendCoreAsync(
            "ReceiveNotification",
            It.Is<object?[]>(a =>
                (string)a[0]! == "Title" &&
                (string)a[1]! == "Body" &&
                (string)a[2]! == "warning"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyUserAsync_DefaultSeverityIsInfo()
    {
        var mockProxy = new Mock<IClientProxy>();
        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockProxy.Object);

        var mockHub = new Mock<IHubContext<NotificationHub>>();
        mockHub.Setup(h => h.Clients).Returns(mockClients.Object);

        var notifier = new HubRealtimeNotifier(mockHub.Object);
        await notifier.NotifyUserAsync("user-1", "T", "M"); // no severity arg

        mockProxy.Verify(p => p.SendCoreAsync(
            "ReceiveNotification",
            It.Is<object?[]>(a => (string)a[2]! == "info"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyUserAsync_UsesUserPrefixedGroup()
    {
        var mockProxy = new Mock<IClientProxy>();
        var mockClients = new Mock<IHubClients>();

        // Verify the group name is exactly "user:{userId}"
        mockClients.Setup(c => c.Group("user:abc-123")).Returns(mockProxy.Object);

        var mockHub = new Mock<IHubContext<NotificationHub>>();
        mockHub.Setup(h => h.Clients).Returns(mockClients.Object);

        var notifier = new HubRealtimeNotifier(mockHub.Object);
        await notifier.NotifyUserAsync("abc-123", "T", "M");

        mockClients.Verify(c => c.Group("user:abc-123"), Times.Once);
    }
}

// ── Integration (end-to-end) tests ────────────────────────────────────────────

/// <summary>
/// End-to-end tests for the SignalR notification hub.
///
/// Approach:
///   - The WebApplicationFactory's in-process TestServer handles SignalR connections
///     via long-polling transport (WebSockets require a real network listener).
///   - factory.Server.CreateHandler() returns an HttpMessageHandler that routes all
///     requests through the in-process server — no real TCP port needed.
///   - A TaskCompletionSource captures the first ReceiveNotification message.
/// </summary>
[Collection("Integration")]
public class NotificationHubIntegrationTests
{
    private readonly AuthWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public NotificationHubIntegrationTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Hub_ConnectedUser_ReceivesNotification_WhenAdminRevokesAllSessions()
    {
        // ── 1. Seed target user and admin ────────────────────────────────────
        var (user, userPwd) = await _factory.SeedConfirmedUserAsync(
            username: $"hub_user_{Guid.NewGuid():N}",
            email: $"hub_user_{Guid.NewGuid():N}@example.com");

        var (admin, adminPwd) = await _factory.SeedAdminUserAsync(
            username: $"hub_admin_{Guid.NewGuid():N}",
            email: $"hub_admin_{Guid.NewGuid():N}@example.com");

        // ── 2. Get user's access token ────────────────────────────────────────
        var userLogin = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(user.UserName!, userPwd));
        userLogin.EnsureSuccessStatusCode();
        var userTokens = await userLogin.Content.ReadFromJsonAsync<TokenDto>(JsonOpts);

        // ── 3. Get admin's access token ───────────────────────────────────────
        var adminLogin = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(admin.UserName!, adminPwd));
        adminLogin.EnsureSuccessStatusCode();
        var adminTokens = await adminLogin.Content.ReadFromJsonAsync<TokenDto>(JsonOpts);

        // ── 4. Connect user to the hub (in-process via long-polling) ─────────
        var received = new TaskCompletionSource<(string Title, string Message, string Severity)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/notifications", options =>
            {
                // Route through the in-process test server — no real TCP needed.
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                // Long-polling works over plain HttpMessageHandler; WebSockets need a network listener.
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(userTokens!.AccessToken);
            })
            .Build();

        connection.On<string, string, string>("ReceiveNotification",
            (title, message, severity) => received.TrySetResult((title, message, severity)));

        await connection.StartAsync();

        // ── 5. Admin revokes all sessions for the user ────────────────────────
        var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminTokens!.AccessToken);

        var revokeResp = await adminClient.DeleteAsync($"/api/admin/users/{user.Id}/sessions");
        revokeResp.EnsureSuccessStatusCode();

        // ── 6. Verify the hub delivered the notification ──────────────────────
        var (title, message, severity) = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        title.Should().Be("All Sessions Revoked");
        message.Should().Contain("administrator");
        severity.Should().Be("error");

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Hub_ConnectedUser_ReceivesNotification_WhenAdminAssignsRole()
    {
        var (user, userPwd) = await _factory.SeedConfirmedUserAsync(
            username: $"hub_role_{Guid.NewGuid():N}",
            email: $"hub_role_{Guid.NewGuid():N}@example.com");

        var (admin, adminPwd) = await _factory.SeedAdminUserAsync(
            username: $"hub_ra_{Guid.NewGuid():N}",
            email: $"hub_ra_{Guid.NewGuid():N}@example.com");

        var userLogin = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(user.UserName!, userPwd));
        var userTokens = await userLogin.Content.ReadFromJsonAsync<TokenDto>(JsonOpts);

        var adminLogin = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(admin.UserName!, adminPwd));
        var adminTokens = await adminLogin.Content.ReadFromJsonAsync<TokenDto>(JsonOpts);

        var received = new TaskCompletionSource<(string Title, string Message, string Severity)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/notifications", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(userTokens!.AccessToken);
            })
            .Build();

        connection.On<string, string, string>("ReceiveNotification",
            (title, msg, sev) => received.TrySetResult((title, msg, sev)));

        await connection.StartAsync();

        var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminTokens!.AccessToken);

        var assignResp = await adminClient.PostAsJsonAsync(
            $"/api/admin/users/{user.Id}/roles", new AdminAssignRoleDto("Admin"));
        assignResp.EnsureSuccessStatusCode();

        var (title, _, severity) = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        title.Should().Be("Role Assigned");
        severity.Should().Be("info");

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Hub_UnauthenticatedConnection_IsRejected()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/notifications", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                // No access token — should be rejected
            })
            .Build();

        var act = async () => await connection.StartAsync();

        await act.Should().ThrowAsync<Exception>();
        await connection.DisposeAsync();
    }
}
