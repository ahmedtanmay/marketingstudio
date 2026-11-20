# Database Design Document

## Collections
| Domain | Collections |
|---|---|
| Identity | users, refreshTokens |
| Tenancy | tenants |
| Brands & Products | brands, products |
| Knowledge | documents, faqs (vectors in Qdrant) |
| Agents | agents, agentRuns, conversations |
| Content & Campaigns | contentItems, campaigns |
| Channels | channelConnections |
| Analytics | analytics |
| Billing | plans, subscriptions, creditLedger, usage, billingEvents |
| Platform | auditLog, _migrations, Hangfire collections |

Field-level definitions: see MongoDB Collection Schemas.md

## Multi-Tenant Strategy
Every document contains TenantId.
