using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using System.Globalization;
using WebClient;
using WebClient.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiUrl = builder.Configuration["Backend:ApiUrl"] ?? "https://localhost:7170/api";

// ── HTTP Clients ──────────────────────────────────────────────────────────────

// "public" — no auth handler; used for login, register, refresh
builder.Services.AddHttpClient("public", client =>
    client.BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/"));

// "api" — has AuthHttpMessageHandler; used for authenticated calls
builder.Services.AddHttpClient("api", client =>
    client.BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/"))
    .AddHttpMessageHandler<AuthHttpMessageHandler>();

// ── Auth Services ─────────────────────────────────────────────────────────────

builder.Services.AddScoped<TokenStorageService>();
builder.Services.AddScoped<JwtAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<JwtAuthenticationStateProvider>());
builder.Services.AddScoped<AuthHttpMessageHandler>();

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

// ── API Services ──────────────────────────────────────────────────────────────

builder.Services.AddScoped<IAuthApiService, AuthApiService>();
builder.Services.AddScoped<IUserApiService, UserApiService>();
builder.Services.AddScoped<ICountryApiService, CountryApiService>();
builder.Services.AddScoped<IPasskeyApiService, PasskeyApiService>();
builder.Services.AddScoped<IAdminApiService, AdminApiService>();

// ── App Services ──────────────────────────────────────────────────────────────

builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<NotificationHubService>();
builder.Services.AddLocalization();

// ── MudBlazor ─────────────────────────────────────────────────────────────────

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.PreventDuplicates = false;
    config.SnackbarConfiguration.NewestOnTop = true;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 4000;
    config.SnackbarConfiguration.HideTransitionDuration = 500;
    config.SnackbarConfiguration.ShowTransitionDuration = 500;
});

var host = builder.Build();

// ── Restore persisted culture (must run before the app renders) ───────────────
var js = host.Services.GetRequiredService<Microsoft.JSInterop.IJSRuntime>();
var cultureName = await js.InvokeAsync<string?>("localStorage.getItem", new object?[] { "app-culture" });
if (!string.IsNullOrEmpty(cultureName))
{
    try
    {
        var culture = new CultureInfo(cultureName);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
    catch (CultureNotFoundException) { /* ignore unknown culture names */ }
}

// ── Kick off SignalR for already-authenticated sessions (page refresh) ────────
var authProvider = host.Services.GetRequiredService<JwtAuthenticationStateProvider>();
var initialState = await authProvider.GetAuthenticationStateAsync();
if (initialState.User.Identity?.IsAuthenticated == true)
{
    var hubService = host.Services.GetRequiredService<NotificationHubService>();
    await hubService.StartAsync();
}

await host.RunAsync();
