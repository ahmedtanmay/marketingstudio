# ASP.NET Core Clean Architecture

## Purpose
This document defines the backend architecture for the AI Marketing Studio platform using ASP.NET Core 10 (LTS), Clean Architecture, CQRS, MongoDB, Redis, Qdrant, MinIO, and AI integrations.

## Architecture Principles
- Separation of Concerns
- Dependency Inversion
- Domain-Driven Design (DDD)
- SOLID Principles
- CQRS Pattern
- Multi-Tenant Ready

## Solution Structure
```text
src
├── Api
├── Application
├── Domain
├── Infrastructure
├── AgentEngine
└── Tests
```

## Domain Layer
Contains core entities:
- Brand
- Product
- Campaign
- Agent
- Tenant
- Subscription

## Application Layer
Contains:
- Commands
- Queries
- DTOs
- Validators
- Interfaces

## Infrastructure Layer
Contains:
- MongoDB
- Redis
- OpenAI
- Qdrant
- MinIO
- External Integrations

## API Layer
Endpoints:
- /api/v1/auth
- /api/v1/brands
- /api/v1/products
- /api/v1/campaigns
- /api/v1/agents

## CQRS
Commands modify data.
Queries read data.

## Authentication
- ASP.NET Identity
- JWT
- Refresh Tokens

ASP.NET Core Identity has no built-in MongoDB store. Use a MongoDB Identity provider
(e.g. AspNetCore.Identity.MongoDbCore) or implement a custom `IUserStore` / `IRoleStore`.
Decide this before Phase 1.

## Multi-Tenant Strategy
All documents contain TenantId.
Tenant context is resolved from JWT.

## Redis Usage
- Caching
- Sessions
- Rate Limiting

## Qdrant Usage
- Knowledge Base Embeddings
- Semantic Search
- RAG Support

## MinIO Usage
- Images
- Videos
- Documents
- Branding Assets

## Logging
Use Serilog with structured logging.

## Testing
- xUnit
- Moq (or NSubstitute)
- FluentAssertions 7.x / AwesomeAssertions (FluentAssertions 8+ needs a commercial licence)
- Testcontainers, WireMock.Net
- Full approach: see Test Strategy.md

## Deployment
Docker Compose + Nginx + Ubuntu.
