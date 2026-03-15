# Auth Starter Template — Roadmap

Legend: ✅ Done · 🔄 In Progress · ⬜ Pending

---

## ✅ Completed

- JWT authentication (access + rotating refresh tokens)
- User registration with email confirmation
- Login (password-based) with email confirmed guard
- Forgot password / reset password flows
- Change password
- Refresh token rotation with server-side revocation on logout
- Rate limiting on auth endpoints
- Account lockout after failed attempts
- User profile (edit personal info, address, date of birth, phone)
- Profile photo upload — swappable Database / S3 storage
- Country selector on profile — wired to `/api/countries`
- Passkey (WebAuthn) — register, login, list, rename, remove (Fido2NetLib)
- Email confirmed guard on login page (targeted warning + resend hint)
- MudBlazor UI — custom theme, dark/light mode persisted to localStorage
- Responsive drawer layout with AppBar, NotificationBell, AuthHeader avatar menu
- Clean Architecture — Domain / Application / Infrastructure / SharedKernel / Api / WebClient
- Session management — IP/UserAgent/LastUsed on RefreshToken, `sid` JWT claim, Sessions tab on Profile
- Roles — `Admin` + `User` seeded at startup, assigned on register, role badge on Profile

---

## ✅ Phase 3 — Admin Panel

> Admin users can manage users, sessions, and roles.

- [x] **Backend** — `GET /api/admin/users` — paginated user list (id, name, email, roles, status, lastLogin)
- [x] **Backend** — `GET /api/admin/users/{id}` — full user details including active sessions
- [x] **Backend** — `DELETE /api/admin/users/{id}/sessions` — revoke all sessions for a user
- [x] **Backend** — `DELETE /api/admin/users/{id}/sessions/{sessionId}` — revoke one session for a user
- [x] **Backend** — `POST /api/admin/users/{id}/roles` — assign role
- [x] **Backend** — `DELETE /api/admin/users/{id}/roles/{role}` — remove role
- [x] **Frontend** — `/admin/users` — users list page with search + pagination
- [x] **Frontend** — `/admin/users/{id}` — user detail page (sessions, roles, account info)
- [x] **Frontend** — Role assignment UI (chip selector)

---

## ✅ Phase 4 — Auditability

> Every significant action is recorded with who, what, when, and from where.

- [x] **Domain** — `AuditLog` entity: `Id`, `UserId`, `Action`, `EntityType`, `EntityId`, `IpAddress`, `UserAgent`, `Timestamp`, `Details` (JSON)
- [x] **Infrastructure** — `AuditLogConfiguration` (EF config, no soft-delete cascade)
- [x] **Infrastructure** — `IAuditLogRepository` + implementation
- [x] **Application** — `IAuditLogService.RecordAsync(userId, action, ...)`
- [x] **Capture** — Login success/failure, logout, password change, password reset, profile update, role assigned/removed, token revoked, passkey added/removed
- [x] **Backend** — `GET /api/admin/logs` — paginated audit log (filterable by user/action/date)
- [x] **Frontend** — `/admin/logs` — audit log viewer with filters

---

## ✅ Phase 5 — Background Tasks (Quartz.NET)

> Scheduled jobs keep the database clean and handle deferred work.

- [x] **Infrastructure** — Add `Quartz` + `Quartz.Extensions.Hosting` NuGet packages
- [x] **Job** — `ExpiredTokenCleanupJob` — delete refresh tokens where `ExpiresUtc < now - RetentionDays` or (`RevokedAtUtc IS NOT NULL` AND `RevokedAtUtc < now - RetentionDays`)
- [x] **Config** — `Quartz:TokenCleanup:CronSchedule` and `Quartz:TokenCleanup:RetentionDays` in appsettings
- [x] **Job** — `ExpiredAuditLogArchiveJob` (Phase 4 dependent) — archive/purge old audit records per retention policy
- [x] **Registration** — Wire jobs into `DependencyInjection.cs`

---

## ✅ Phase 6 — Two-Factor Authentication (TOTP)

> Time-based OTP via authenticator app (Google Authenticator, Authy, etc.). Identity already has the primitives.

- [x] **Backend** — `GET /api/auth/2fa/setup` — generate TOTP secret + QR code URI (QRCoder PNG, base64)
- [x] **Backend** — `POST /api/auth/2fa/enable` — verify first TOTP code + enable 2FA
- [x] **Backend** — `POST /api/auth/2fa/disable` — verify TOTP code + disable, resets authenticator key
- [x] **Backend** — Login flow: if 2FA enabled, return `requiresTwoFactor: true` instead of tokens; second step `POST /api/auth/2fa/verify`
- [x] **Frontend** — 2FA setup flow on Profile Security tab (QR code display + manual key + verification input)
- [x] **Frontend** — 2FA challenge step on Login page (step-2 form with back button)

---

## ✅ Phase 7 — GDPR / Account Self-Service

> Users can delete their account and export their data.

- [x] **Backend** — `DELETE /api/users/me` — hard-delete user + cascade (tokens, passkeys, image; audit logs anonymised via FK SetNull)
- [x] **Backend** — `GET /api/users/me/export` — JSON export of all user data (profile, sessions, passkeys, audit history)
- [x] **Frontend** — "Delete my account" confirmation dialog on Profile → Account tab (email confirmation required)
- [x] **Frontend** — "Export my data" button on Profile → Account tab (downloads account-export.json)

---

## ✅ Phase 8 — Developer Experience

> Makes the template easier to clone, configure, and extend.

- [x] Unit tests — `AuthService`, `TokenService`, `UserProfileService` (xUnit + Moq)
- [x] Integration tests — Auth flow end-to-end (WebApplicationFactory)
- [x] `docker-compose.yml` — API + SQL Server for zero-config local dev
- [x] `Dockerfile` for the API project
- [x] GitHub Actions CI — build + test on push to `main` and `development`
- [x] Swagger / OpenAPI — annotated with `[ProducesResponseType]` on all controllers
- [x] Health check endpoint — `GET /health` (DB connectivity, disk)

---

## Nice-to-Have / Future Consideration ⬜

- [ ] Social login — OAuth2 (Google, Microsoft, GitHub) via `AddAuthentication().AddGoogle()`
- [ ] Email OTP — alternative to TOTP, send a 6-digit code via email
- [ ] "Remember me" extended refresh token lifetime option
- [ ] New device / new IP login email notification
- [ ] Account lockout email notification (you've been locked out, here's an unlock link)
- [x] Localization / i18n skeleton
- [x] SignalR hub — real-time notification delivery (replace in-memory `NotificationService`)
- [ ] API versioning (`/api/v1/...`)
- [ ] Configurable password policy in appsettings (min length, complexity requirements)
- [ ] Request/response logging middleware with correlation IDs
- [ ] Read-only "impersonate user" mode for admins (view as user, no writes)

---

## Notes

- Roles are **hardcoded** (`Admin`, `User`) — seeded at startup, not from appsettings. Developers extend by adding to the seed list in code.
- Quartz jobs run **in-process** (same host as API). For high-scale apps, extract to a separate worker service.
- Audit log `Details` column stores JSON — keeps the schema flat while allowing rich capture per action type.
- Session metadata (IP, UserAgent) is captured from `IHttpContextAccessor` — already registered in `Api/Program.cs`.
