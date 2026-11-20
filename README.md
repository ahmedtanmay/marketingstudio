# AI Marketing Studio — Backend

ASP.NET Core 10 API for the AI Marketing Studio platform. Design documents are in [`docs/`](docs/)
(start with [`System Architecture Document.md`](docs/System%20Architecture%20Document.md); open decisions are in
[`Decision Register.md`](docs/Decision%20Register.md)).

## Solution layout

```text
src/
  MarketingStudio.Domain          Entities and rules (no framework dependencies)
  MarketingStudio.Application     Use cases (MediatR commands/queries), validators, interfaces
  MarketingStudio.Infrastructure  MongoDB, Identity store, JWT/refresh tokens, email, audit log
  MarketingStudio.Api             HTTP endpoints, auth, tenant context, error handling, rate limits
tests/
  MarketingStudio.UnitTests         Fast tests, no Docker
  MarketingStudio.IntegrationTests  Real API + real MongoDB (Testcontainers; needs Docker)
```

## Run locally

Prerequisites: .NET 10 SDK, Docker Desktop.

```powershell
docker compose up -d mongodb redis mailpit      # MongoDB (replica set), Redis, Mailpit
dotnet run --project src/MarketingStudio.Api     # http://localhost:5283
```

- Emails (verification, password reset) appear in Mailpit: http://localhost:8025
- Development creates a JWT signing key and Data Protection keys under `src/MarketingStudio.Api/.local/` (git-ignored).
- Health: `GET /health/live`, `GET /health/ready`

Everything in containers: `docker compose up -d --build` (API on http://localhost:8080).

## Tests

```powershell
dotnet test tests/MarketingStudio.UnitTests          # no Docker needed
dotnet test tests/MarketingStudio.IntegrationTests   # starts a MongoDB container
```

**Windows with Smart App Control on:** it blocks unsigned NuGet DLLs such as `MongoDB.Bson.dll`, so run the
integration tests in the Linux SDK container instead (same as CI):

```powershell
./scripts/test-in-docker.ps1
```

Test names carry story IDs (`[Trait("Story", "AUTH-2")]`) from `User Stories and Acceptance Criteria.md`.

## API (Sprint 1)

| Method | Path | Auth | Story |
|---|---|---|---|
| POST | `/api/v1/auth/register` | public | AUTH-1 |
| POST | `/api/v1/auth/verify-email` | public | AUTH-1 |
| POST | `/api/v1/auth/resend-verification` | signed in | AUTH-1 |
| POST | `/api/v1/auth/login` | public | AUTH-2 |
| POST | `/api/v1/auth/refresh` | refresh cookie | AUTH-3 |
| POST | `/api/v1/auth/logout` | refresh cookie | AUTH-3 |
| POST | `/api/v1/auth/forgot-password` | public | AUTH-4 |
| POST | `/api/v1/auth/reset-password` | public | AUTH-4 |
| GET | `/api/v1/me` | signed in | TEN-3, BIL-1 |

Security behaviour (see `Security Architecture.md`):
- Access token: ES256 JWT, 15 minutes, claims `sub`, `tid`, `role`, `jti`.
- Refresh token: opaque, HttpOnly + Secure + SameSite=Strict cookie on `/api/v1/auth`, 7 days, rotated on every use,
  30-day absolute session; reusing a rotated token revokes the whole session family.
- Lockout after 5 failed sign-ins for 15 minutes; all sign-in failures return the same message.
- Registration and forgot-password respond identically whether or not the email exists.
- Password: ≥ 10 characters, not on the common-password list (starter list — replace before launch).
- Tenant comes only from the token; tenant-owned reads go through `TenantScopedRepository`.

## Defaults chosen in Sprint 1 (Decision Register)

| Decision | Default | Change by |
|---|---|---|
| D2 MediatR licence | MediatR **12.5.0** (last Apache-2.0 version) | Tech lead |
| D3 Assertions | FluentAssertions **7.2.0** (Apache-2.0) | Tech lead |
| D4 Mocking | NSubstitute | Tech lead |
| D5 Identity on MongoDB | Custom `MongoUserStore` | Tech lead |
| D1 Email provider | SMTP sender (Mailpit locally); Postmark/SES SMTP when chosen | Tech lead |
| D28 Framework | .NET 10 LTS | — |

## Not done yet in Sprint 1
- Meta Business Verification, Stripe account, hosting provider — owner actions, not code.
- Staging server provisioning and deploy job (needs hosting decision D6).
- CI runs on every push and pull request: https://github.com/ahmedtanmay/marketingstudio/actions
