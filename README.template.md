# AuthStarterTemplate

A full-stack authentication system built on **.NET 10**, **Blazor WebAssembly**, and **Clean Architecture**.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- SQL Server (LocalDB is fine for development)
- *(Optional)* Redis — for distributed caching
- *(Optional)* An S3-compatible bucket — for cloud profile image storage

### 1. Create the Initial Migration

Since this template ships without EF Core migrations, create one for your database:

```bash
cd src/Api
dotnet ef migrations add InitialCreate
dotnet ef database update
```

Then seed the country lookup table:

```bash
# Run in SQL Server Management Studio or sqlcmd:
# sql scripts/InsertAppCountries.sql
```

### 2. Set User Secrets

Run the following from the `src/Api` directory. These values must **never** be in source control.

```bash
cd src/Api

dotnet user-secrets set "Jwt:SecretKey"                        "your-256-bit-secret-minimum-32-characters"
dotnet user-secrets set "Jwt:Issuer"                           "https://localhost:7170"
dotnet user-secrets set "Jwt:Audience"                         "https://localhost:7060"
dotnet user-secrets set "ConnectionStrings:DefaultConnection"  "Server=(localdb)\\mssqllocaldb;Database=AuthStarterTemplate;Trusted_Connection=True;"
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

### 3. Run

```bash
# Terminal 1 — API (https://localhost:7170)
cd src/Api && dotnet run

# Terminal 2 — Blazor client (https://localhost:7060)
cd WebClient && dotnet run
```

Or open `AuthStarterTemplate.slnx` in Visual Studio 2022 and configure multiple startup projects.

---

## What's Inside

### Authentication & Identity

| Feature | Details |
|---|---|
| **Registration** | Email confirmation required before first login |
| **Login** | JWT access token (15 min) + refresh token (7 days) |
| **Token Rotation** | Refresh tokens are single-use; revoked on rotation |
| **Silent Refresh** | Expired tokens refreshed transparently in the background |
| **Concurrent 401 Safety** | `SemaphoreSlim` prevents race conditions when multiple requests expire simultaneously |
| **Logout** | Server-side refresh token revocation (`[AllowAnonymous]` — works even with an expired access token) |
| **Forgot / Reset Password** | Secure email link flow |
| **Change Password** | From the Profile page |
| **Account Lockout** | 5 failed attempts → 15-minute lockout |
| **Passkeys (WebAuthn)** | FIDO2 passwordless login — register, authenticate, rename, delete credentials |
| **Two-Factor Auth (TOTP)** | Authenticator app setup with QR code, enable/disable, backup recovery codes |

### Session Management

- Refresh tokens track **IP address**, **device info**, and **user agent**
- **Profile → Security tab**: view all active sessions with device/IP/timestamp
- Revoke individual sessions or **revoke all** (security wipe)

### Roles & Authorization

- **Admin** and **User** roles seeded at startup
- **User** role assigned automatically on registration
- `[Authorize(Roles = "Admin")]` guards all admin endpoints

### Admin Panel

- User list with search and pagination
- Assign / remove roles per user
- Revoke sessions — individual or all sessions for any user
- Audit log viewer with filtering by action, user, and date range
- Real-time notifications via SignalR to affected users

### API Infrastructure

| Feature | Details |
|---|---|
| **Rate Limiting** | Sliding window per endpoint |
| **IP Filtering** | Allowlist or blocklist mode with CIDR support |
| **Idempotency Keys** | `X-Idempotency-Key` deduplication for POST/PUT/PATCH |
| **Correlation IDs** | `X-Correlation-ID` on every request/response |
| **API Versioning** | URL-segment versioning (`/api/v1/`) |
| **Output Caching** | Configurable TTL on public endpoints |
| **Redis Cache** | Opt-in distributed cache |
| **Request/Response Logging** | Serilog structured logging |
| **Password Policy** | Configurable via `appsettings.json` |

### GDPR

- **Account deletion** — removes the user, all tokens, audit log entries, and profile image
- **Data export** — one-click JSON download of all personal data

### Background Jobs (Quartz.NET)

- **`ExpiredTokenCleanupJob`** — purges expired refresh tokens on a configurable schedule

### Real-Time (SignalR)

- JWT-authenticated WebSocket hub with per-user group routing
- `IRealtimeNotifier` interface keeps the hub abstracted from business logic

### Frontend (Blazor WASM + MudBlazor 9)

- Custom JWT `AuthenticationStateProvider`
- Dual HTTP clients: public (anonymous) and api (Bearer + transparent refresh)
- Dark / light mode toggle
- Language switcher (EN / FR / ES)
- Notification bell with unread badge

### Testing

- **Unit tests** — xUnit + Moq
- **Integration tests** — WebApplicationFactory with InMemory EF Core

---

## Docker

```bash
docker-compose up --build
```

Starts API on `http://localhost:8080`, SQL Server on `localhost:1433`, and Redis on `localhost:6379`.

---

## Running Tests

```bash
dotnet test tests/Unit/Unit.Tests.csproj

dotnet test tests/Integration/Integration.Tests.csproj \
  --environment "Jwt__SecretKey=test-secret-key-minimum-32-characters"
```

---

## Configuration Reference

| Key | Default | Description |
|---|---|---|
| `Jwt:SecretKey` | — | HMAC-SHA256 signing key (min 32 bytes) — **use secrets** |
| `Jwt:Issuer` | — | Token issuer |
| `Jwt:Audience` | — | Token audience |
| `Jwt:ExpirationMinutes` | `15` | Access token lifetime |
| `Jwt:ExpirationDays` | `7` | Refresh token lifetime |
| `ConnectionStrings:DefaultConnection` | — | SQL Server connection string |
| `AllowedOrigins` | `[]` | CORS allowed origins (array) |
| `App:ApiBaseUrl` | — | Used to build absolute profile image URLs |
| `App:ProfileImageStorage` | `"Database"` | `"Database"` or `"S3"` |
| `Redis:Enabled` | `false` | Enable Redis distributed cache |
| `Redis:ConnectionString` | — | Redis connection string |
| `Smtp:Host` | — | SMTP hostname |
| `Smtp:Port` | `587` | SMTP port |
| `IpFiltering:Enabled` | `false` | Enable IP allow/blocklist |
| `Idempotency:Enabled` | `false` | Enable idempotency key deduplication |
| `PasswordPolicy:RequiredLength` | `8` | Minimum password length |
| `TokenCleanup:CronSchedule` | `"0 0 3 * * ?"` | Quartz CRON for token cleanup |
| `TokenCleanup:RetentionDays` | `1` | Days to keep expired tokens |

---

## License

This project is licensed under the [MIT License](LICENSE.txt).
