# OpenAPI Specification

All endpoints are versioned under `/api/v1` (see AspNetCore_Clean_Architecture.md).

## Authentication
POST /api/v1/auth/register
POST /api/v1/auth/login
POST /api/v1/auth/refresh
POST /api/v1/auth/logout

## Brands
GET /api/v1/brands
GET /api/v1/brands/{id}
POST /api/v1/brands
PUT /api/v1/brands/{id}
DELETE /api/v1/brands/{id}

## Products
GET /api/v1/products
GET /api/v1/products/{id}
POST /api/v1/products
PUT /api/v1/products/{id}
DELETE /api/v1/products/{id}

## Agents
GET /api/v1/agents
POST /api/v1/agents
POST /api/v1/agents/{id}/execute
GET /api/v1/agents/runs/{runId}

## Campaigns
GET /api/v1/campaigns
POST /api/v1/campaigns

## Knowledge Base
See Knowledge Base Design.md for details.
POST /api/v1/knowledge/documents
GET /api/v1/knowledge/documents?brandId=
GET /api/v1/knowledge/documents/{id}
DELETE /api/v1/knowledge/documents/{id}
POST /api/v1/knowledge/documents/{id}/retry
GET /api/v1/knowledge/faqs?brandId=
POST /api/v1/knowledge/faqs
PUT /api/v1/knowledge/faqs/{id}
DELETE /api/v1/knowledge/faqs/{id}
POST /api/v1/knowledge/search

## Content Approval
GET /api/v1/content?status=pending_review
POST /api/v1/content/{id}/approve
POST /api/v1/content/{id}/reject

## Channels, Scheduling, Inbox, Webhooks
See Social Integrations Design.md for details.
GET /api/v1/channels?brandId=
POST /api/v1/channels/meta/connect
GET /api/v1/channels/meta/callback
POST /api/v1/channels/whatsapp/connect
DELETE /api/v1/channels/{id}
POST /api/v1/content/{id}/schedule
POST /api/v1/content/{id}/cancel
POST /api/v1/content/{id}/retry
GET /api/v1/inbox?brandId=
POST /api/v1/inbox/{conversationId}/reply
GET /api/v1/webhooks/meta
POST /api/v1/webhooks/meta
POST /api/v1/webhooks/meta/data-deletion

## Billing
See Billing and Subscriptions Design.md for details.
GET /api/v1/billing/plans
GET /api/v1/billing/subscription
POST /api/v1/billing/checkout
POST /api/v1/billing/portal
POST /api/v1/billing/change-plan
GET /api/v1/billing/downgrade-preview?plan=
POST /api/v1/billing/credits/checkout
GET /api/v1/billing/credits
GET /api/v1/billing/usage?from=&to=
GET /api/v1/billing/invoices
POST /api/v1/webhooks/stripe
GET /api/v1/admin/tenants/{id}/billing
POST /api/v1/admin/tenants/{id}/credits
PUT /api/v1/admin/tenants/{id}/entitlements

## Still to define
- Request/response schemas, error format, pagination
- Users, Tenants, Analytics endpoints
- Error codes: `PLAN_LIMIT_REACHED`, `QUOTA_EXCEEDED` (HTTP 402) defined in Billing design; full error catalogue pending
