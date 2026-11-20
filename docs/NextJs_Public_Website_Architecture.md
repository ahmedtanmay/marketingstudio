# Next.js Public Website Architecture

## Purpose
This document defines the architecture of the public-facing Marketing Studio website.

## Objectives
- SEO optimization
- Lead generation
- Product marketing
- Documentation hosting
- Blog publishing
- User acquisition funnel

## Technology Stack
- Next.js 15+
- TypeScript
- Tailwind CSS
- App Router
- MDX
- Google Analytics

## Site Map
```text
/
/features
/pricing
/customers
/blog
/docs
/contact
/signup
/login
```

## Architecture
```text
marketingstudio.ai
├── Landing Pages
├── Blog
├── Docs
├── Pricing
└── Auth Redirect
```

## App Router Structure
```text
app
├── page.tsx
├── features
├── pricing
├── blog
├── docs
├── contact
├── login
└── signup
```

## SEO Strategy
- Server-side rendering
- Metadata API
- Open Graph tags
- Structured data
- Sitemap generation

## Lead Generation
- Newsletter signup
- Demo request forms
- Contact forms
- Free trial registration

## Authentication Handoff
The public site does not authenticate users. Authentication is owned by the Angular portal
(see Angular_Enterprise_Architecture.md).

- `/login` redirects to `https://app.marketingstudio.ai/login`
- `/signup` redirects to `https://app.marketingstudio.ai/signup` (plan selection passed as a query parameter)
- Lead-capture forms (newsletter, demo, contact) post to the API without requiring login

## Content Strategy
### Blog
- AI Marketing
- Social Media Automation
- SaaS Growth
- Case Studies

### Documentation
- Product Guides
- API Documentation
- Tutorials

## Analytics
- Google Analytics
- Microsoft Clarity
- Conversion Tracking

## Performance Targets
- Lighthouse > 90
- First Contentful Paint < 2s
- Core Web Vitals compliant

## Deployment
- Docker Container
- Nginx Reverse Proxy
- Separate deployment from Angular Portal
