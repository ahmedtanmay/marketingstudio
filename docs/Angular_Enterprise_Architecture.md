# Angular Enterprise Architecture

## Purpose
This document defines the Angular architecture for the AI Marketing Studio application portal.

## Technology Stack
- Angular 19+
- Angular Material
- NgRx
- RxJS
- SCSS
- ngx-translate
- SignalR Client
- Chart.js

## Architecture Principles
- Feature-first architecture
- Separation of concerns
- Multi-tenant ready
- Reusable shared components
- Strict TypeScript

## Workspace Structure
```text
src
├── app
│   ├── core
│   ├── shared
│   ├── features
│   ├── state
│   └── layout
└── assets
```

## Core Module
Contains:
- Authentication
- Authorization
- HTTP Interceptors
- API Services
- Logging
- Configuration

## Feature Modules
- Dashboard
- Brands
- Products
- Campaigns
- Agents
- Analytics
- Billing
- Settings

## State Management
Use NgRx for:
- Authentication State
- Tenant Context
- Brand Context
- Agent State
- Campaign State

## Authentication Flow
Login -> JWT -> Refresh Token -> Route Guard

## Multi-Tenant Design
Each user is associated with a tenant.
Tenant branding includes:
- Logo
- Primary Color
- Secondary Color
- Theme

## Dashboard Widgets
- Campaign Performance
- Lead Statistics
- AI Usage Metrics
- Revenue Summary
- Agent Activity

## Testing Strategy
- Jest
- Angular Testing Library
- Cypress E2E (+ cypress-axe for accessibility)
- Full approach: see Test Strategy.md

## Deployment
Build Angular SPA and serve through Nginx.
