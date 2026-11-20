# Software Requirements Specification (SRS)

## System Overview
AI Marketing Studio is a multi-tenant SaaS platform for creating and managing AI-powered marketing agents.

## Architecture

### Public Web Layer
- Next.js
- SEO Optimization
- Marketing Website
- Blog Platform
- Lead Generation

### Application Layer
- Angular 19+
- Angular Material
- RxJS
- NgRx
- Enterprise Dashboard

### Backend Layer
- ASP.NET Core 10 (LTS; .NET 9 reached end of support in November 2026)
- Clean Architecture
- CQRS
- MediatR

## Functional Modules

### Brand Management
- Create/Edit Brands
- Brand Assets
- Brand Voice Configuration

### Product Management
- CRUD Operations
- Media Upload
- Product Categorization

### AI Agent Management
- Planner Agent (routes requests to specialist agents)
- Content Agent
- Campaign Agent
- Social Agent
- Support Agent
- Analytics Agent
- Detailed specs: see Agent Specification.md

### Analytics
- Engagement Reporting
- Conversion Analytics
- Campaign Performance

### Detailed Requirements
User stories, acceptance criteria and non-functional requirements: see User Stories and Acceptance Criteria.md

## Technology Stack

### Public Website
- Next.js
- TypeScript
- Tailwind CSS

### Application Portal
- Angular 19+
- Angular Material
- NgRx
- RxJS

### Backend
- ASP.NET Core 10 (LTS)

### Databases
- MongoDB (Self Hosted)
- Qdrant
- Redis

### Storage
- MinIO

### Scheduler
- Hangfire
