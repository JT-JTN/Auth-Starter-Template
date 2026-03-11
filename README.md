# Auth Starter Template

A production-ready full-stack authentication template built with **.NET 10**, **Blazor WebAssembly**, and **MudBlazor**. Clone it, configure it, and ship — the auth plumbing is already done.

---

## What's Included

### Authentication & Security
- **Register** with email confirmation (no auto-login until email is verified)
- **Login** with JWT access token + refresh token rotation
- **Silent token refresh** — expired access tokens are automatically refreshed in the background; users never see an auth redirect mid-session
- **Logout** with server-side refresh token revocation
- **Forgot password** / **Reset password** via email link
- **Change password** from the profile page
- **Account lockout** after 5 failed attempts (15 min lockout)
- **Rate limiting** on all auth endpoints (10 req/min; 3 req/min on forgot-password)
- **Email confirmation** enforced on login

### User Profiles
- View and edit profile (first name, last name, phone, date of birth, full address)
- **Profile image upload** (JPEG, PNG, WebP, GIF — 5 MB limit)
- Storage strategy switchable at runtime:
  - `Database` — stored in SQL Server, served via API
  - `S3` — stored in any S3-compatible bucket (Cloudflare R2, AWS S3, MinIO, etc.)
- Delete profile image
- Member since / last login tracking

### Frontend (Blazor WASM + MudBlazor 9)
- Custom JWT-based `AuthenticationStateProvider` — no OIDC dependencies
- Dual HTTP clients: `public` (no auth) and `api` (Bearer + silent refresh handler)
- Dark / light mode toggle, persisted to `localStorage`
- Steam-style account menu in the AppBar (avatar → name → dropdown)
- Notification bell with badge (ready to wire to real notifications)
- Fully responsive layout with collapsible drawer

### Backend (ASP.NET Core + Clean Architecture)
- Clean Architecture: `Domain / Application / Infrastructure / Api / SharedKernel`
- ASP.NET Core Identity + JWT Bearer (correctly configured alongside Identity's cookie defaults)
- EF Core + SQL Server with explicit table configurations
- Serilog structured logging — console + rolling file sink
- Global exception handling middleware with consistent `{ title, status, traceId }` error shape
- CORS policy configured for the Blazor client
- Country lookup table (`AppCountry`) with seeding script

---

## Tech Stack

| Layer | Technology |
|---|---|
| API | ASP.NET Core 10, C# |
| ORM | Entity Framework Core 10 |
| Database | SQL Server |
| Auth | ASP.NET Core Identity + JWT Bearer |
| Logging | Serilog (Console + File) |
| Frontend | Blazor WebAssembly (.NET 10) |
| UI Components | MudBlazor 9 |
| Object Storage | SQL Server (default) or S3-compatible |

---

## Getting Started

### Prerequisites
- .NET 10 SDK
- SQL Server (LocalDB works fine for development)

### 1. Clone and configure User Secrets

The template uses [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) to keep sensitive values out of source control. Run these from the `src/Api` directory:

```bash
dotnet user-secrets set "Jwt:SecretKey"            "your-256-bit-secret-at-least-32-chars"
dotnet user-secrets set "Jwt:Issuer"               "https://localhost:7170"
dotnet user-secrets set "Jwt:Audience"             "https://localhost:7060"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\\mssqllocaldb;Database=AuthStarter;Trusted_Connection=True;"
dotnet user-secrets set "AllowedOrigins:0"         "https://localhost:7060"
dotnet user-secrets set "AllowedOrigins:1"         "http://localhost:5060"
```

For email (required for confirmation and password reset):
```bash
dotnet user-secrets set "Smtp:Host"       "smtp.example.com"
dotnet user-secrets set "Smtp:Port"       "587"
dotnet user-secrets set "Smtp:Username"   "you@example.com"
dotnet user-secrets set "Smtp:Password"   "your-smtp-password"
dotnet user-secrets set "Smtp:FromEmail"  "noreply@example.com"
dotnet user-secrets set "Smtp:FromName"   "Your App Name"
```

### 2. Apply database migrations

```bash
cd src/Api
dotnet ef database update
```

Then optionally seed the countries lookup table:
```sql
-- Run: sql scripts/InsertAppCountries.sql
```

### 3. Run both projects

```bash
# Terminal 1 — API
cd src/Api
dotnet run

# Terminal 2 — Blazor client
cd WebClient
dotnet run
```

Or open the solution in Visual Studio and run multiple startup projects.

---

## Configuration Reference

All keys live in `appsettings.json` / `appsettings.Development.json`. Real values go in User Secrets or environment variables — never in source control.

| Key | Description |
|---|---|
| `Jwt:SecretKey` | HMAC-SHA256 signing key — minimum 32 bytes |
| `Jwt:Issuer` | Token issuer (typically the API base URL) |
| `Jwt:Audience` | Token audience (typically the frontend URL) |
| `Jwt:ExpirationMinutes` | Access token lifetime (default: 15) |
| `Jwt:ExpirationDays` | Refresh token lifetime (default: 7) |
| `ConnectionStrings:DefaultConnection` | SQL Server connection string |
| `AllowedOrigins` | Array of frontend origins for CORS |
| `App:ApiBaseUrl` | API base URL — used to build profile image URLs |
| `App:ProfileImageStorage` | `"Database"` or `"S3"` |
| `S3:ServiceUrl` | S3-compatible endpoint URL |
| `S3:AccessKey` | S3 access key |
| `S3:SecretKey` | S3 secret key |
| `S3:BucketName` | S3 bucket name |
| `S3:Region` | S3 region |
| `S3:PublicBaseUrl` | Public CDN/base URL for serving images |
| `Smtp:Host` | SMTP server hostname |
| `Smtp:Port` | SMTP port (typically 587) |
| `Smtp:Username` | SMTP login username |
| `Smtp:Password` | SMTP login password |
| `Smtp:FromEmail` | Sender email address |
| `Smtp:FromName` | Sender display name |

---

## NuGet Packages

### Api
| Package | Version | Purpose |
|---|---|---|
| `Serilog.AspNetCore` | 9.0.0 | Structured logging with request pipeline integration |
| `Serilog.Sinks.File` | 6.0.0 | Rolling file sink for log output |
| `Swashbuckle.AspNetCore` | 10.1.4 | Swagger / OpenAPI docs |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.3 | EF Core CLI tooling (migrations) |

### Infrastructure
| Package | Version | Purpose |
|---|---|---|
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.3 | JWT Bearer token validation |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 10.0.3 | ASP.NET Core Identity + EF Core stores |
| `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.3 | SQL Server EF Core provider |
| `Microsoft.EntityFrameworkCore.Tools` | 10.0.3 | EF Core CLI tooling |
| `Microsoft.Extensions.Identity.Core` | 10.0.3 | Identity core abstractions |
| `Microsoft.Extensions.Identity.Stores` | 10.0.3 | Identity store abstractions |
| `AWSSDK.S3` | 3.7.415.8 | S3-compatible object storage (profile images) |
| `MailKit` | 4.15.1 | SMTP email sending |
| `MimeKit` | 4.15.1 | MIME message construction |

### Domain
| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Extensions.Identity.Stores` | 10.0.3 | Identity entity base types |

### WebClient (Blazor WASM)
| Package | Version | Purpose |
|---|---|---|
| `Microsoft.AspNetCore.Components.WebAssembly` | 10.0.2 | Blazor WebAssembly runtime |
| `Microsoft.AspNetCore.Components.WebAssembly.DevServer` | 10.0.2 | Dev-time hot reload server |
| `Microsoft.AspNetCore.Components.Authorization` | 10.0.2 | `AuthorizeView`, `AuthenticationStateProvider` |
| `Microsoft.Extensions.Http` | 10.0.0 | `IHttpClientFactory` for named HTTP clients |
| `MudBlazor` | 9.1.0 | Material Design component library |

---

## Project Structure

```
src/
  Api/                    → ASP.NET Core Web API
    Controllers/          → AuthController, UsersController, CountriesController
    Middleware/           → ExceptionHandlingMiddleware
  Application/            → Use cases, interfaces, DTOs (no framework dependencies)
  Domain/                 → Entities, domain exceptions (pure C#)
  Infrastructure/         → EF Core, Identity, JWT, email, image storage
  SharedKernel/           → Result<T>, value objects, shared abstractions
WebClient/                → Blazor WebAssembly frontend
  Components/             → AuthHeader, NotificationBell
  Layout/                 → MainLayout, NavMenu
  Pages/                  → Login, Register, ConfirmEmail, ForgotPassword,
                            ResetPassword, Profile, Home
  Services/               → Auth/User API clients, token storage, auth state
sql scripts/              → Country seed data
assets/                   → Design source files (favicon, icons)
```

---

## License

MIT
