# AI Marketing Studio

ASP.NET Core 10 API and Angular 22 portal for the AI Marketing Studio platform. Design documents are in
[`docs/`](docs/) (start with [`System Architecture Document.md`](docs/System%20Architecture%20Document.md); open
decisions are in [`Decision Register.md`](docs/Decision%20Register.md)).

## Solution layout

```text
src/
  MarketingStudio.Domain          Entities, rules, roles → permissions (no framework dependencies)
  MarketingStudio.Application     Use cases (MediatR commands/queries), validators, interfaces
  MarketingStudio.Infrastructure  MongoDB, Identity store, JWT/refresh tokens, email, audit log
  MarketingStudio.Api             HTTP endpoints, auth, permission policies, error handling, rate limits
tests/
  MarketingStudio.UnitTests         Fast tests, no Docker
  MarketingStudio.IntegrationTests  Real API + real MongoDB (Testcontainers; needs Docker)
portal/                             Angular 22 portal (Material 3, NgRx Signals, Vitest)
  e2e/                              Cypress journeys + axe accessibility checks
```

## Run locally

Prerequisites: .NET 10 SDK, Node 24, Docker Desktop.

```powershell
docker compose up -d mongodb redis mailpit seaweedfs   # MongoDB, Redis, Mailpit, SeaweedFS (S3)
dotnet run --project src/MarketingStudio.Api     # API on http://localhost:5283
cd portal; npm ci; npx ng serve                  # portal on http://localhost:4200 (proxies /api to the API)
```

**Windows with Smart App Control on:** run the API in Docker instead of `dotnet run` and point the portal at it:

```powershell
docker compose up -d --build mongodb mailpit seaweedfs api   # API on http://localhost:8080
cd portal; $env:API_URL = 'http://localhost:8080'; npx ng serve
```

- Emails (verification, password reset, invites) appear in Mailpit: http://localhost:8025
- Development creates a JWT signing key and Data Protection keys under `src/MarketingStudio.Api/.local/` (git-ignored),
  or in the `api-keys` volume when the API runs in Docker.
- Health: `GET /health/live`, `GET /health/ready`
- The portal keeps the access token in memory only; the refresh token is an HttpOnly cookie. The dev-server proxy
  keeps portal and API on one origin, as Nginx does in production.

## Tests

```powershell
dotnet test tests/MarketingStudio.UnitTests          # no Docker needed
dotnet test tests/MarketingStudio.IntegrationTests   # starts a MongoDB container
cd portal; npx ng test --watch=false                 # portal unit tests (Vitest)
```

**Windows with Smart App Control on:** it blocks unsigned NuGet DLLs such as `MongoDB.Bson.dll`, so run the
integration tests in the Linux SDK container instead (same as CI):

```powershell
./scripts/test-in-docker.ps1
```

**End-to-end (Cypress + axe):** with the stack running as above (portal started with
`--host 0.0.0.0 --allowed-hosts host.docker.internal`), run `./portal/e2e/run-in-docker.ps1`.

CI runs the backend suites, the portal build and unit tests, the end-to-end journeys against the real API container,
vulnerable-package checks, a secret scan and the Docker build on every push and pull request:
https://github.com/ahmedtanmay/marketingstudio/actions

Test names carry story IDs (`[Trait("Story", "AUTH-2")]`, `[AUTH-5]` in Cypress) from
`User Stories and Acceptance Criteria.md`.

## API

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
| GET | `/api/v1/me` (incl. permissions) | signed in | TEN-3, BIL-1 |
| GET | `/api/v1/users` | `users.manage` | AUTH-6 |
| PUT | `/api/v1/users/{id}/role` | `users.manage` | AUTH-6 |
| POST | `/api/v1/users/{id}/deactivate`, `/reactivate` | `users.manage` | AUTH-6 |
| GET / POST | `/api/v1/invites` | `users.manage` | AUTH-5 |
| POST | `/api/v1/invites/{id}/resend` | `users.manage` | AUTH-5 |
| DELETE | `/api/v1/invites/{id}` | `users.manage` | AUTH-5 |
| GET | `/api/v1/invites/lookup?token=` | public (token) | AUTH-5 |
| POST | `/api/v1/invites/accept` | public (token) | AUTH-5 |
| GET | `/api/v1/onboarding` | signed in | TEN-1 |
| GET / POST | `/api/v1/brands` | `brands.read` / `brands.write` | BRD-1 |
| GET / PUT | `/api/v1/brands/{id}` | `brands.read` / `brands.write` | BRD-1 |
| PUT | `/api/v1/brands/{id}/voice` | `brands.content.write` | BRD-2 |
| DELETE | `/api/v1/brands/{id}?confirmName=` | `brands.delete` | BRD-5 |
| GET / POST / PUT / DELETE | `/api/v1/brands/{id}/products[/{productId}]` | `products.read` / `products.write` | PRD-1 |
| GET | `/api/v1/brands/{id}/products/categories` | `products.read` | PRD-1 |
| GET / POST (multipart) | `/api/v1/brands/{id}/media` | `brands.read` / `brands.content.write` | BRD-3 |
| PUT / DELETE | `/api/v1/brands/{id}/media/{assetId}[/tags]` | `brands.content.write` | BRD-3 |
| GET | `/api/v1/media/files/{token}` | public (signed, 1 h) | BRD-3 |

Endpoints require permissions, never role names; the role → permission map in
`src/MarketingStudio.Domain/Users/Permissions.cs` is the code form of `docs/RBAC Matrix.md`. `RbacTests` checks every
protected endpoint with every role and fails the build if a new endpoint has no entry.

Security behaviour (see `Security Architecture.md`):
- Access token: ES256 JWT, 15 minutes, claims `sub`, `tid`, `role`, `jti`.
- Refresh token: opaque, HttpOnly + Secure + SameSite=Strict cookie on `/api/v1/auth`, 7 days, rotated on every use,
  30-day absolute session; reusing a rotated token revokes the whole session family.
- Lockout after 5 failed sign-ins for 15 minutes; all sign-in failures return the same message.
- Registration and forgot-password respond identically whether or not the email exists.
- Password: ≥ 10 characters, not on the common-password list (starter list — replace before launch).
- Media: JPEG/PNG/WebP ≤ 10 MB, type detected from the bytes; files stay in private object storage and are served only through signed links that expire after an hour.
- Invites: hashed single-use tokens valid 7 days; pending invites reserve a seat; 402 `PLAN_LIMIT_REACHED` when full.
- The last active Tenant Admin cannot be downgraded or deactivated; deactivation revokes refresh tokens at once.
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

## Not done yet
- Razorpay account in test mode (payment provider changed from Stripe; billing docs are updated in Sprint 9) — owner action, not code.
- Domain registration and Meta Business Verification — postponed to Sprint 5 by owner decision (Decision Register D29, D30).
- Staging/production server and deploy job — postponed; will be self-hosted on the owner's own machine with a public IP
  (D6). Plan: Ubuntu Server + Docker Compose + Nginx, GitHub Actions self-hosted runner, off-site backups.
