# RBAC Matrix

| Role | Brands | Products | Campaigns | Agents | Content Approval | Analytics | Billing |
|------|--------|----------|-----------|--------|------------------|-----------|---------|
| Super Admin | Full | Full | Full | Full | Approve | Full | Full |
| Tenant Admin | Full | Full | Full | Full | Approve | Full | Manage |
| Marketing Manager | Read/Write | Read/Write | Read/Write | Execute | Approve | Read | None |
| Content Manager | Content Only | Read | Read | Execute | Submit | Read | None |
| Viewer | Read | Read | Read | Read | None | Read | None |

## Definitions
- **Full**: create, read, update, delete, configure.
- **Content Only**: read brands and edit brand content assets (voice guidelines, knowledge-base documents, media); cannot create/delete brands or change brand settings.
- **Execute**: run agents; cannot create or reconfigure them.
- **Submit**: send generated content for review; cannot publish.
- **Approve**: approve/reject content for publishing.
- **Manage**: view invoices, change plan and payment method.

## Channels and Inbox
| Role | Connect/Disconnect Channels | Schedule Posts | WhatsApp Inbox |
|------|-----------------------------|----------------|----------------|
| Super Admin | Full | Full | Reply |
| Tenant Admin | Full | Full | Reply |
| Marketing Manager | None | Full | Reply |
| Content Manager | None | None | Read |
| Viewer | None | None | None |

Super Admin is a platform-level role (across tenants); all other roles are scoped to a single tenant.
