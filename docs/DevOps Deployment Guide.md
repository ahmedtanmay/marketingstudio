# DevOps Deployment Guide

## Purpose
How AI Marketing Studio is built, deployed, configured, backed up, monitored and operated. Server sizing and costs are in Infrastructure Costing.md; test gates in Test Strategy.md.

## Environments
| Environment | Host | Deploys from | Data |
|---|---|---|---|
| Local | Developer machine (Docker Compose) | Working copy | Seed data |
| Staging | Staging server (4 vCPU / 16 GB) | Every merge to `main` | Synthetic data; Meta test app, Stripe test mode |
| Production | Production server (8 vCPU / 32 GB + 500 GB volume) | Tagged release (`v*`) after manual approval | Real |

## Server Baseline
- **Ubuntu Server 24.04 LTS**, Docker Engine + Docker Compose plugin.
- Hardening: SSH keys only (no passwords, no root login), firewall allows 22 (restricted IPs), 80, 443 only; fail2ban; unattended security upgrades; time sync.
- Data volumes on **encrypted disks (LUKS)**; SeaweedFS on the attached 500 GB volume.
- Separate non-root deploy user; Docker socket not exposed to containers except where unavoidable.

## Containers (Docker Compose)
| Service | Image | Exposed | Notes |
|---|---|---|---|
| nginx | nginx (stable) | 80, 443 | TLS via Let's Encrypt (certbot), routing, rate limits, security headers |
| api | ghcr.io/…/api | internal | ASP.NET Core API + SignalR + Hangfire servers + Agent Engine; 2 instances for rolling deploy |
| portal | (static files served by nginx) | — | Angular build copied into the nginx image |
| website | ghcr.io/…/website | internal | Next.js SSR |
| mongodb | mongo (pinned major) | internal | Single node with replica set enabled (needed for transactions) |
| redis | redis (pinned) | internal | AOF persistence; password |
| qdrant | qdrant/qdrant (pinned) | internal | API key; snapshots enabled |
| seaweedfs | chrislusf/seaweedfs (pinned) | internal | S3 API; bucket per environment. Files are served only through API signed links |
| clamav | clamav/clamav (pinned) | internal | Signature updates daily |
| prometheus, grafana, loki, promtail | pinned | internal (Grafana via VPN/SSH tunnel or IP allow-list) | Monitoring stack |

All images pinned by version (and digest for third-party images); upgrades go through staging first.

## Nginx Routing
| Host / path | Target |
|---|---|
| `marketingstudio.ai` | website |
| `app.marketingstudio.ai/` | Angular static files (SPA fallback to `index.html`) |
| `app.marketingstudio.ai/api/v1/*` | api (upstream with 2 instances) |
| `app.marketingstudio.ai/hubs/*` | api (WebSocket upgrade, sticky by connection) |
| `app.marketingstudio.ai/hangfire` | api — Super Admin only, plus IP allow-list |
| `app.marketingstudio.ai/api/v1/media/files/*` | api (signed, expiring links; no separate media host) |

---

## CI/CD (GitHub Actions)

```text
Pull request ─▶ build · lint · unit · integration (Testcontainers) · contract · security scans · E2E core
                 (gates: Test Strategy.md → CI Pipeline and Quality Gates)

Merge to main ─▶ build images (api, website, nginx+portal) tagged with git SHA
              ─▶ push to GitHub Container Registry
              ─▶ deploy to staging ─▶ smoke tests ─▶ nightly suites

Tag v*       ─▶ promote the same images (no rebuild) ─▶ manual approval (GitHub environment)
              ─▶ deploy to production ─▶ smoke tests ─▶ notify
```

### Deployment steps (per environment)
1. CI connects via SSH (deploy key restricted to the deploy user) and updates the image tags in `.env.release`.
2. `docker compose pull`.
3. **Database migrations:** a one-off `api migrate` container applies pending index and data migrations (versioned migration classes, recorded in a `_migrations` collection). Migrations must be **backward-compatible** with the previous release (expand → migrate → contract over two releases).
4. **Rolling API update:** restart `api-1`, wait for `/health/ready`, then `api-2`; Nginx keeps serving from the healthy instance. Hangfire jobs resume automatically (persistent storage).
5. Website and nginx containers recreated (seconds of downtime acceptable for the website; portal static files are swapped atomically).
6. Smoke tests: health endpoints, sign-in with a synthetic user, plans endpoint, one Qdrant search, one signed media link.

### Rollback
- Re-deploy the previous image tag (kept in GHCR); takes < 5 minutes.
- Because migrations are backward-compatible, rollback does not require a database restore. Data-destructive migrations are forbidden in the same release as the code that stops using the data.

## Configuration and Secrets
- Non-secret config: `appsettings.{Environment}.json` + environment variables in Compose.
- Secrets: **Docker Secrets** files on the server (root-only, 0400), mounted into containers; populated from the team password manager — never committed.
- Secrets list: MongoDB/Redis/Qdrant/SeaweedFS credentials, JWT signing key, Data Protection master key, OpenAI and fallback LLM keys, Meta app secret and webhook verify token, Stripe secret and webhook secret, email API key.
- Feature flags (configuration-based) for risky features: auto-reply, Reasoning tier, new prompt versions.

## Backups
| Data | Method | Frequency | Retention |
|---|---|---|---|
| MongoDB | `mongodump` (full) + oplog-based incremental | Daily full, hourly incremental | 30 days |
| Qdrant | Collection snapshots | Daily | 14 days (re-creatable from documents) |
| SeaweedFS | Mirror to off-site object storage (`mc mirror`) | Hourly | 30 days with versioning |
| Redis | AOF/RDB file copy | Daily | 7 days (cache/locks — not critical) |
| Server config | Git (infrastructure repo) | On change | — |

- Backups are **encrypted** before upload to off-site storage in a **different location**.
- Backup job success/failure is reported to Prometheus; failure alerts.
- **Restore drill quarterly** to a scratch server; confirms RPO ≤ 1 h, RTO ≤ 4 h (Test Strategy.md).

## Monitoring and Alerting
| Signal | Source | Alert (examples) |
|---|---|---|
| Host CPU, RAM, disk, network | node-exporter | Disk > 80%, RAM > 90% for 10 min |
| Containers | cAdvisor | Container restarting |
| API | ASP.NET Core metrics / OpenTelemetry | Error rate > 2% (5 min), p95 > 1 s |
| Jobs | Hangfire metrics | Failed jobs spike; queue backlog > 15 min |
| AI | Usage Meter metrics | Fallback active > 10 min; daily spend > cap |
| Publishing | Custom metrics | Failure rate > 5% (1 h); publish delay > 5 min |
| Data stores | MongoDB, Redis, Qdrant exporters | Replication/oplog window, memory, slow queries |
| Backups | Backup job metrics | Missed or failed backup |
| Certificates | Blackbox exporter | Expiry < 14 days |
| Uptime | External uptime monitor (outside the server) | Portal/API/website down > 2 min |

- Alerts → email + team chat; S1 alerts also to on-call phone.
- Grafana dashboards: platform overview, API, jobs, AI cost & usage, publishing, billing, data stores.
- **Logs:** Serilog JSON → Promtail → Loki, 30-day retention; no secrets or content bodies in logs.
- **Traces:** OpenTelemetry with `runId`/`tenantId` correlation (sampling 10% normally, 100% on errors).

## Operations
### Runbooks (to be written in Sprint 10)
- Deploy and rollback
- Restore MongoDB / SeaweedFS / Qdrant from backup
- Rotate a leaked secret (per secret type)
- AI provider outage (force fallback, pause queues)
- Meta token invalidation wave / app restricted
- Stripe webhook backlog / reconciliation
- Disk full, MongoDB memory pressure
- Tenant data export and deletion

### On-call (from launch)
- Rotating weekly on-call among developers; response targets: S1 15 min, S2 1 h (business hours for S3/S4).
- Post-launch: daily review of errors, AI cost and publish failures for the first 2 weeks.

## Scaling Steps
Follow Infrastructure Costing.md → Scaling Plan. Before multi-node: move Hangfire to dedicated worker containers, add Redis backplane for SignalR, convert MongoDB to a 3-node replica set, and place Nginx (or a managed load balancer) in front of multiple app nodes.
