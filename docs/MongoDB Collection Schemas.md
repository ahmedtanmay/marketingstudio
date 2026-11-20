# MongoDB Collection Schemas

## users
- _id
- tenantId (null for Super Admin)
- email (unique)
- passwordHash
- displayName
- roles
- status
- lastLoginAt
- createdAt

## refreshTokens
- _id
- userId
- tenantId
- tokenHash
- familyId (all rotations of one sign-in)
- expiresAt
- revokedAt
- replacedBy
- createdAt

## auditLog
Append-only; retained 1 year.
- _id
- tenantId (null for platform events)
- actorUserId
- action (e.g. content.approved, user.role_changed, channel.disconnected)
- targetType
- targetId
- metadata
- ip
- createdAt

## tenants
- _id
- name
- status
- subscriptionId (ref subscriptions)
- entitlementOverrides (enterprise only)
- createdAt

## plans
- _id (e.g. "starter")
- name
- entitlements (map of entitlement key -> value)
- stripePriceIdMonthly
- stripePriceIdAnnual
- isPublic
- active

## subscriptions
- _id
- tenantId
- planId
- interval (month | year)
- status (trialing | active | past_due | suspended | cancelled | expired)
- provider (stripe)
- providerCustomerId
- providerSubscriptionId
- trialEndsAt
- currentPeriodStart
- currentPeriodEnd
- cancelAtPeriodEnd
- pendingPlanId (scheduled downgrade)
- graceEndsAt
- updatedAt

## creditLedger
Append-only.
- _id
- tenantId
- type (grant | consume | expire | adjust)
- source (allowance | topup | promo | agentRun | ingestion | admin)
- credits (positive for grant, negative for consume/expire)
- referenceId (runId / documentId / checkout session id)
- expiresAt (grants only)
- reason (adjust only)
- createdBy
- createdAt

## billingEvents
- _id (provider event id, unique)
- type
- receivedAt
- processedAt
- status

## brands
- _id
- tenantId
- name
- industry
- toneOfVoice
- languages
- timezone (IANA, e.g. "Asia/Kolkata")

## agents
- _id
- tenantId
- brandId
- agentType
- configuration

## products
- _id
- tenantId
- brandId
- name
- description
- category
- price
- media

## campaigns
- _id
- tenantId
- brandId
- campaignType
- status
- schedule

## documents
- _id
- tenantId
- brandId
- fileName
- storageKey (MinIO object key)
- contentType
- sizeBytes
- checksum (SHA-256)
- version
- ingestionStatus (uploaded | processing | ready | failed | rejected | deleting)
- failureReason
- chunkCount
- language
- uploadedBy
- createdAt

## faqs
- _id
- tenantId
- brandId
- question
- answer
- createdBy
- updatedAt

## conversations
- _id
- tenantId
- brandId
- agentId
- userId
- channel (portal | whatsapp)
- channelConnectionId
- customerPhone (masked in logs)
- status (open | awaiting_human | resolved)
- lastCustomerMessageAt (for WhatsApp 24-hour window)
- messages
- createdAt

## channelConnections
- _id
- tenantId
- brandId
- platform (facebook | instagram | whatsapp)
- externalId (Page id / IG user id / WhatsApp phone number id)
- displayName
- encryptedToken
- scopes
- status (active | expiring | disconnected)
- connectedBy
- connectedAt
- lastCheckedAt

## contentItems
- _id
- tenantId
- brandId
- campaignId
- agentRunId
- channel
- body
- media
- status (draft | pending_review | approved | rejected | scheduled | published | failed | cancelled)
- sourceRefs (knowledge chunks used)
- reviewedBy
- channelConnectionId
- scheduledAt
- hangfireJobId
- publishAttempts
- lastError
- platformPostId
- permalink
- publishedAt

## agentRuns
- _id
- tenantId
- brandId
- agentType
- userId
- input
- output
- status
- promptId
- promptVersion
- provider
- model
- usedFallback
- tokensIn
- tokensOut
- costUsd
- startedAt
- completedAt

## usage
Daily rollup per tenant, used for quotas and billing.
- _id
- tenantId
- date
- agentType
- runs
- tokensIn
- tokensOut
- credits
- costUsd

## analytics
- _id
- tenantId
- brandId
- campaignId
- channel
- metric
- value
- recordedAt

## Still to define
Field types, required fields, indexes (every collection: compound index starting with tenantId), validation rules.
