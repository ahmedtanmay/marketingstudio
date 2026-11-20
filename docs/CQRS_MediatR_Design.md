# CQRS_MediatR_Design

## Purpose
Define CQRS and MediatR patterns for AI Marketing Studio.

## Architecture Overview

```text
Controller
  -> MediatR
     -> Pipeline Behaviors
        -> Command/Query Handler
           -> Repository
              -> MongoDB
```

## CQRS Principles
- Commands change state
- Queries read state
- Separate handlers
- DTO-based responses

## Folder Structure
```text
Application
├── Brands
│   ├── Commands
│   ├── Queries
│   ├── DTOs
│   └── Validators
├── Products
├── Campaigns
└── Agents
```

## Command Example
```csharp
public record CreateBrandCommand(
 string Name,
 string Industry
) : IRequest<string>;
```

## Query Example
```csharp
public record GetBrandQuery(string Id)
 : IRequest<BrandDto>;
```

## Pipeline Behaviors
- Validation
- Logging
- Authorization
- Tenant Resolution
- Exception Handling

## Validation
Use FluentValidation for all commands.

## Multi-Tenant Strategy
- TenantId in JWT
- TenantId on all documents
- Tenant-aware repositories

## Domain Events
- BrandCreatedEvent
- CampaignCreatedEvent
- ProductAddedEvent

## Integration Events
- FacebookPostPublished
- InstagramPostPublished
- EmailCampaignCreated

## Testing
- xUnit
- Moq (or NSubstitute)
- FluentAssertions 7.x / AwesomeAssertions (FluentAssertions 8+ needs a commercial licence)
- Full approach: see Test Strategy.md

## Recommended Packages
- MediatR (licence note: MediatR moved to commercial licensing from v13 with a free tier for smaller companies — verify current terms before Phase 1; alternatives are pinning 12.x or a source-generated mediator such as `Mediator`)
- FluentValidation
- Mapster
- Serilog
- Polly

## Benefits
- Scalability
- Maintainability
- Testability
- Clean separation of responsibilities
