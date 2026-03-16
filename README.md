# ASP.NET Core Auth Starter

[![CI](https://github.com/JohnThomasJTN/Auth-Starter-Template/actions/workflows/ci.yml/badge.svg)](https://github.com/JohnThomasJTN/Auth-Starter-Template/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/JTNetworx.AuthStarterTemplate?label=nuget)](https://www.nuget.org/packages/JTNetworx.AuthStarterTemplate)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)

A production-ready, full-stack authentication starter built on **.NET 10**, **Blazor WebAssembly**, and **Clean Architecture**. Everything you need to get from zero to a secure, scalable auth system — without writing the same boilerplate for the hundredth time.

---

## Quick Start

### Install as a `dotnet new` template

```bash
dotnet new install JTNetworx.AuthStarterTemplate
dotnet new auth-starter -n MyApp
cd MyApp
```

### Or clone directly

```bash
git clone https://github.com/JohnThomasJTN/Auth-Starter-Template.git
cd Auth-Starter-Template
```

---

## What's Inside

### Authentication & Identity

| Feature | Details |
|---|---|
| **Registration** | Email confirmation required before first login |
| **Login** | JWT access token (15 min) + refresh token (7 days) |
| **Token Rotation** | Refresh tokens are single-use; revoked on rotation |
| **Silent Refresh** | Expired tokens refreshed transparently in the background — users never get bounced mid-session |
| **Concurrent 401 Safety** | `SemaphoreSlim` prevents race conditions when multiple requests expire simultaneously |
| **Logout** | Server-side refresh token revocation (`[AllowAnonymous]` — works even with an expired access token) |
| **Forgot / Reset Password** | Secure email link flow |
| **Change Password** | From the Profile page |
| **Account Lockout** | 5 failed attempts → 15-minute lockout |
| **Email Confirmation Guard** | Login returns `403` with a specific error if email is unconfirmed |
| **Passkeys (WebAuthn)** | FIDO2 passwordless login — register, authenticate, rename, delete credentials |
| **Two-Factor Auth (TOTP)** | Authenticator app setup with QR code, enable/disable, backup recovery codes |
| **JWT Claim Mapping** | `MapInboundClaims = false` — `sub`, `role`, `sid` claims work correctly without URI remapping |

### Session Management

- Refresh tokens track **IP address**, **device info**, and **user agent**
- **Profile → Security tab**: view all active sessions with device/IP/timestamp
- Revoke individual sessions or **revoke all** (security wipe)
- Current session highlighted

### Roles & Authorization

- **Admin** and **User** roles seeded at startup
- **User** role assigned automatically on registration
- `[Authorize(Roles = "Admin")]` guards all admin endpoints
- Role assignment/removal surfaced in the admin panel and Profile page

### Admin Panel

- **User list** with search and pagination
- **Assign / remove roles** per user
- **Revoke sessions** — individual or all sessions for any user
- **Audit log viewer** with filtering by action, user, and date range
- Admin actions push **real-time notifications** to affected users via SignalR

### Audit Log

Every significant action is recorded:

| Action | Captured |
|---|---|
| Login / Register | ✅ |
| Password change / reset | ✅ |
| Role assignment / removal | ✅ |
| Session revocation | ✅ |
| Profile update | ✅ |
| Account deletion | ✅ |
| 2FA enable / disable | ✅ |

### Background Jobs (Quartz.NET)

- **`ExpiredTokenCleanupJob`** — purges expired refresh tokens on a configurable schedule
- Schedule and retention period controlled via `appsettings.json`

### GDPR

- **Account deletion** — removes the user, all tokens, audit log entries, and profile image
- **Data export** — one-click JSON download of all personal data held for the authenticated user

### API Infrastructure

| Feature | Details |
|---|---|
| **Rate Limiting** | Sliding window per endpoint — 10 req/min on auth, 3 req/min on forgot-password |
| **IP Filtering** | Allowlist or blocklist mode with CIDR support, toggle via `IpFiltering:Enabled` |
| **Idempotency Keys** | `X-Idempotency-Key` deduplication for POST/PUT/PATCH; backed by `IDistributedCache` |
| **Correlation IDs** | `X-Correlation-ID` on every request/response, injected into Serilog `LogContext` |
| **Problem Details** | All errors return RFC 7807 `{ type, title, status, detail, traceId }` |
| **API Versioning** | URL-segment versioning (`/api/v1/`), query-string fallback |
| **Output Caching** | Countries endpoint and other public data cached with configurable TTL |
| **Redis Cache** | Opt-in distributed cache — powers output cache and idempotency store when enabled |
| **Request/Response Logging** | Serilog structured logging with method, path, status, and duration |
| **Password Policy** | Configurable length, complexity, and history requirements via `appsettings.json` |

### Real-Time (SignalR)

- `NotificationHub` with JWT authentication over WebSockets (`?access_token=` query param)
- Per-user group routing (`user:{id}`)
- `IRealtimeNotifier` interface keeps the hub abstracted from business logic
- `NoOpRealtimeNotifier` fallback for test hosts and background workers
- Admin actions (role change, session revocation) push live notifications to affected users

### User Profiles

- Edit first name, last name, phone, date of birth, full address, country
- **Profile image upload** — JPEG, PNG, WebP, GIF (5 MB limit)
- Storage backend switchable at runtime:
  - `Database` — stored as bytes in SQL Server, served via API
  - `S3` — any S3-compatible bucket (Cloudflare R2, AWS S3, MinIO, etc.)

### Frontend (Blazor WASM + MudBlazor 9)

- Custom JWT `AuthenticationStateProvider` — no OIDC dependencies, no cookies
- Dual HTTP clients: `public` (anonymous) and `api` (Bearer + transparent refresh)
- Steam-style account menu in the AppBar (avatar → name → role chip → dropdown)
- Notification bell with unread badge and dropdown
- **Language switcher** — EN / FR / ES (persisted to `localStorage`, full globalization data)
- Dark / light mode toggle (persisted to `localStorage`)
- Fully responsive layout with collapsible drawer
- All pages have proper loading states and error handling

### Testing

- **Unit tests** — `xUnit` + `Moq`; domain logic, token service, result types, audit service
- **Integration tests** — `WebApplicationFactory` with InMemory EF Core; covers full auth flows
- **Hub unit tests** — mock `IHubContext<NotificationHub>`; verify group routing and payload
- **Hub integration tests** — long-polling via `factory.Server.CreateHandler()`; end-to-end notification delivery
- Rate limiter and Serilog race conditions both addressed in the test harness

### Developer Experience

- **Swagger / OpenAPI** — available at `/swagger` in development
- **Docker** — `Dockerfile` for the API; `docker-compose.yml` for API + SQL Server + Redis
- **GitHub Actions CI** — restore → build → unit tests → integration tests → upload `.trx` results
- **Serilog** — structured logging to console and rolling file sink

### Localization

- Resource files for `en`, `fr`, and `es` on both API and WebClient
- Globe icon in the AppBar cycles cultures and persists to `localStorage`
- `<BlazorWebAssemblyLoadAllGlobalizationData>true</BlazorWebAssemblyLoadAllGlobalizationData>` enabled for full ICU support

---

## Tech Stack

| Layer | Technology |
|---|---|
| API | ASP.NET Core 10 |
| Architecture | Clean Architecture (Domain / Application / Infrastructure / SharedKernel / Api) |
| ORM | Entity Framework Core 10 |
| Database | SQL Server |
| Auth | ASP.NET Core Identity + JWT Bearer |
| Passkeys | .NET 10 `SignInManager` WebAuthn methods |
| 2FA | TOTP via `Microsoft.AspNetCore.Identity` |
| Background Jobs | Quartz.NET |
| Real-Time | ASP.NET Core SignalR |
| Caching | `IDistributedCache` (memory or Redis) |
| Logging | Serilog (Console + Rolling File) |
| Frontend | Blazor WebAssembly (.NET 10) |
| UI Components | MudBlazor 9 |
| Object Storage | SQL Server (default) or S3-compatible |
| Testing | xUnit, Moq, `WebApplicationFactory` |
| CI | GitHub Actions |

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- SQL Server (LocalDB is fine for development)
- *(Optional)* Redis — for distributed caching
- *(Optional)* An S3-compatible bucket — for cloud profile image storage

### 1. Set User Secrets

Run the following from the `src/Api` directory. These values must **never** be in source control.

```bash
cd src/Api

dotnet user-secrets set "Jwt:SecretKey"                        "your-256-bit-secret-minimum-32-characters"
dotnet user-secrets set "Jwt:Issuer"                           "https://localhost:7170"
dotnet user-secrets set "Jwt:Audience"                         "https://localhost:7060"
dotnet user-secrets set "ConnectionStrings:DefaultConnection"  "Server=(localdb)\\mssqllocaldb;Database=AuthStarter;Trusted_Connection=True;"
dotnet user-secrets set "AllowedOrigins:0"                     "https://localhost:7060"
dotnet user-secrets set "AllowedOrigins:1"                     "http://localhost:5060"
```

Email (required for confirmation and password reset):

```bash
dotnet user-secrets set "Smtp:Host"       "smtp.example.com"
dotnet user-secrets set "Smtp:Port"       "587"
dotnet user-secrets set "Smtp:Username"   "you@example.com"
dotnet user-secrets set "Smtp:Password"   "your-smtp-password"
dotnet user-secrets set "Smtp:FromEmail"  "noreply@yourapp.com"
dotnet user-secrets set "Smtp:FromName"   "Your App Name"
```

### 2. Apply Migrations

```bash
cd src/Api
dotnet ef database update
```

Then seed the country lookup table:

```bash
# Run the script in SQL Server Management Studio or sqlcmd:
# sql scripts/InsertAppCountries.sql
```

### 3. Run

```bash
# Terminal 1 — API (https://localhost:7170)
cd src/Api && dotnet run

# Terminal 2 — Blazor client (https://localhost:7060)
cd WebClient && dotnet run
```

Or open `AuthStarterTemplate.slnx` in Visual Studio 2022 and configure multiple startup projects.

---

## Configuration Reference

| Key | Default | Description |
|---|---|---|
| `Jwt:SecretKey` | — | HMAC-SHA256 signing key (min 32 bytes) — **use secrets** |
| `Jwt:Issuer` | — | Token issuer — usually the API base URL |
| `Jwt:Audience` | — | Token audience — usually the frontend URL |
| `Jwt:ExpirationMinutes` | `15` | Access token lifetime |
| `Jwt:ExpirationDays` | `7` | Refresh token lifetime |
| `ConnectionStrings:DefaultConnection` | — | SQL Server connection string |
| `AllowedOrigins` | `[]` | CORS allowed origins (array) |
| `App:ApiBaseUrl` | — | Used to build absolute profile image URLs |
| `App:ProfileImageStorage` | `"Database"` | `"Database"` or `"S3"` |
| `S3:ServiceUrl` | — | S3-compatible endpoint |
| `S3:AccessKey` | — | S3 access key |
| `S3:SecretKey` | — | S3 secret key |
| `S3:BucketName` | — | S3 bucket name |
| `S3:Region` | — | S3 region |
| `S3:PublicBaseUrl` | — | Public CDN base URL for image serving |
| `Redis:Enabled` | `false` | Enable Redis distributed cache |
| `Redis:ConnectionString` | — | Redis connection string |
| `Smtp:Host` | — | SMTP hostname |
| `Smtp:Port` | `587` | SMTP port |
| `Smtp:Username` | — | SMTP username |
| `Smtp:Password` | — | SMTP password |
| `Smtp:FromEmail` | — | Sender email |
| `Smtp:FromName` | — | Sender display name |
| `IpFiltering:Enabled` | `false` | Enable IP allow/blocklist |
| `IpFiltering:Mode` | `"blocklist"` | `"allowlist"` or `"blocklist"` |
| `IpFiltering:Allowlist` | `[]` | IPs/CIDRs to allow |
| `IpFiltering:Blocklist` | `[]` | IPs/CIDRs to block |
| `Idempotency:Enabled` | `false` | Enable idempotency key deduplication |
| `Idempotency:ExpirationMinutes` | `60` | How long to cache idempotent responses |
| `PasswordPolicy:RequiredLength` | `8` | Minimum password length |
| `PasswordPolicy:RequireDigit` | `true` | Require at least one digit |
| `PasswordPolicy:RequireUppercase` | `true` | Require at least one uppercase letter |
| `PasswordPolicy:RequireNonAlphanumeric` | `true` | Require at least one special character |
| `TokenCleanup:CronSchedule` | `"0 0 3 * * ?"` | Quartz CRON for token cleanup (default: 3 AM daily) |
| `TokenCleanup:RetentionDays` | `1` | Days to keep expired tokens before purging |

---

## Project Structure

```
src/
  Api/
    Controllers/        AuthController, UsersController, CountriesController,
                        PasskeyController, AdminController
    Hubs/               NotificationHub (SignalR, JWT auth)
    Middleware/         ExceptionHandling, CorrelationId, IpFiltering,
                        Idempotency, RequestResponseLogging
    Services/           HubRealtimeNotifier
    Program.cs
  Application/
    DTOs/               Auth, User, Passkey, Admin, GDPR DTOs
    Services/           IAuthService, ITokenService, IUserProfileService,
                        IPasskeyService, IAdminService, IAuditLogService,
                        IRealtimeNotifier, IProfileImageStore, IAppCountryService
  Domain/
    Users/              User, RefreshToken, PasskeyCredential, AppCountry,
                        UserProfileImage
    Audit/              AuditLog
    DomainExceptions/   8 typed exceptions extending DomainException
  Infrastructure/
    Persistence/        ApplicationDbContext, EF configurations, repositories
    Services/           AuthService, TokenService, UserProfileService,
                        PasskeyService, AdminService, AuditLogService,
                        AppCountryService, NoOpRealtimeNotifier,
                        DatabaseProfileImageStore, S3ProfileImageStore
    Jobs/               ExpiredTokenCleanupJob (Quartz.NET)
    Options/            AppSettings, S3Settings, RedisSettings, IpFilteringSettings,
                        IdempotencySettings, PasswordPolicySettings, TokenCleanupSettings
  SharedKernel/
    Result.cs           Result<T>, PaginatedResult<T>

WebClient/
  Components/           AuthHeader, NotificationBell, CultureSwitcher
  Layout/               MainLayout, NavMenu
  Pages/                Home, Login, Register, Profile, ConfirmEmail,
                        ForgotPassword, ResetPassword, Admin
  Services/             AuthApiService, UserApiService, CountryApiService,
                        PasskeyApiService, AdminApiService,
                        TokenStorageService, JwtAuthenticationStateProvider,
                        AuthHttpMessageHandler, NotificationHubService,
                        NotificationService
  Resources/            Localizer.resx (en), Localizer.fr.resx, Localizer.es.resx

tests/
  Unit/                 Domain logic, token service, result types, audit service
  Integration/          Full auth flow tests, hub unit + integration tests

sql scripts/
  InsertAppCountries.sql

pack/
  template-pack.csproj  NuGet template packaging

assets/                 favicon, icon
```

---

## Docker

Start everything with a single command:

```bash
docker-compose up --build
```

This starts:
- **API** on `http://localhost:8080`
- **SQL Server** on `localhost:1433`
- **Redis** on `localhost:6379`

> Set `Redis:Enabled = true` in `appsettings.json` (or environment variables) to activate the Redis cache when running via Docker.

---

## Running Tests

```bash
# Unit tests
dotnet test tests/Unit/Unit.Tests.csproj

# Integration tests (uses InMemory EF Core — no real DB required)
dotnet test tests/Integration/Integration.Tests.csproj \
  --environment "Jwt__SecretKey=test-secret-key-minimum-32-characters"
```

CI runs both automatically on every push to `main` and `development`.

---

## Publishing the NuGet Template

The template package is built from `pack/template-pack.csproj`:

```bash
dotnet pack pack/template-pack.csproj -c Release -o ./release-artifacts
dotnet nuget push release-artifacts/JTNetworx.AuthStarterTemplate.*.nupkg \
  --api-key YOUR_NUGET_API_KEY \
  --source https://api.nuget.org/v3/index.json
```

---

## License

This project is licensed under the [MIT License](LICENSE.txt).

Copyright (c) 2025 JT Networx

> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. THE AUTHORS OR COPYRIGHT HOLDERS SHALL NOT BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY ARISING FROM THE USE OF THIS SOFTWARE.
