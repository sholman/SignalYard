# SignalYard - Product Requirements Document

## Overview

SignalYard is a lightweight, self-hosted structured logging tool designed to run in Azure App Service with Azure Table Storage as the backend. It provides a cost-effective alternative to solutions like Seq that require Docker or VM hosting.

## Problem Statement

Existing structured logging solutions (Seq, etc.) require Docker containers or dedicated VMs, adding operational overhead and cost that doesn't make sense for solo developers or small teams. There's a need for a simple, low-cost logging solution that:

- Runs in Azure App Service (no container management)
- Uses inexpensive storage (Table Storage vs SQL)
- Supports structured logging with property querying
- Requires minimal operational maintenance

## Target Users

- Solo developers and small teams
- Projects already running in Azure
- Users who want structured logging without infrastructure complexity

## Goals

1. Provide a simple, deployable logging solution for Azure App Service
2. Minimise storage costs using Azure Table Storage
3. Support standard Serilog ingestion formats
4. Enable efficient log querying by application and time range
5. Secure access via Entra ID (UI) and API keys (ingestion)

## Non-Goals

- Full-text search indexing (client-side filtering is acceptable)
- Real-time log streaming
- Complex alerting or notification systems
- Multi-tenant SaaS deployment

---

## Functional Requirements

### 1. Log Ingestion

**1.1 Ingestion Endpoint**
- POST endpoint at `/api/ingest` accepting batched log events
- Support for Serilog compact JSON format (CLEF)
- Alternative endpoint `/api/events/raw` for newline-delimited JSON

**1.2 Log Event Schema**
| Field | Type | Description |
|-------|------|-------------|
| @t | DateTimeOffset | Timestamp |
| @mt | string | Message template |
| @m | string | Rendered message |
| @l | string | Log level (Verbose, Debug, Information, Warning, Error, Fatal) |
| @x | string | Exception details |
| @i | string | Event ID (optional) |
| ... | object | Additional structured properties |

**1.3 Authentication**
- API key authentication via `X-Api-Key` header
- Keys prefixed with `sy_` for identification
- Keys hashed (SHA256) for storage
- One key per application

### 2. Log Storage

**2.1 Table Storage Schema**

*Logs Table*
| Field | Description |
|-------|-------------|
| PartitionKey | `{ApplicationName}_{YearMonth}` (e.g., `MyApp_202501`) |
| RowKey | `{InvertedTicks}_{Guid}` (newest first ordering) |
| LogTimestamp | Original event timestamp |
| Application | Application name |
| Level | Log level |
| Message | Rendered message |
| MessageTemplate | Original template |
| Exception | Exception details |
| Properties | JSON string of additional properties |

**2.2 Design Rationale**
- Partition by app + month enables efficient time-range queries
- Inverted ticks ensure newest logs returned first
- Monthly partitions simplify retention cleanup (delete entire partitions)

### 3. Log Querying

**3.1 Query Parameters**
| Parameter | Required | Description |
|-----------|----------|-------------|
| application | Yes | Application name |
| from | Yes | Start of date range |
| to | Yes | End of date range |
| level | No | Filter by log level |
| maxResults | No | Limit results (default 1000) |

**3.2 Client-Side Search**
- Text search performed in browser on loaded result set
- Searches message, exception, message template, and properties
- No server-side full-text indexing required

### 4. Application Management

**4.1 Application Entity**
| Field | Description |
|-------|-------------|
| Name | Application identifier |
| Description | Optional description |
| ApiKeyHash | SHA256 hash of API key |
| ApiKeyPrefix | First 12 characters for display (e.g., `sy_abc123...`) |
| Enabled | Whether ingestion is active |
| RetentionDays | Log retention period (default 365) |
| CreatedAt | Creation timestamp |

**4.2 Operations**
- Create application (generates API key)
- Update application settings
- Regenerate API key (invalidates previous key)
- Delete application
- Enable/disable ingestion

### 5. Log Retention

**5.1 Automatic Cleanup**
- Background service runs daily
- Deletes log partitions older than retention period
- Per-application retention settings
- Default retention: 365 days

**5.2 Implementation**
- Identifies partitions (months) fully outside retention window
- Deletes entire partitions for efficiency
- Runs during off-peak hours (configurable delay on startup)

### 6. Authentication & Authorisation

**6.1 UI Authentication**
- Microsoft Entra ID (Azure AD) via OpenID Connect
- All UI routes require authentication
- User identity displayed in navigation

**6.2 Ingestion Authentication**
- API key in `X-Api-Key` header
- Key validated against hashed lookup table
- Returns application name claim on success
- Disabled applications reject ingestion

**6.3 Authorisation Model**
| Resource | Auth Method | Access |
|----------|-------------|--------|
| UI (all pages) | Entra ID | Authenticated users |
| /api/ingest | API Key | Valid, enabled application keys |
| /api/events/raw | API Key | Valid, enabled application keys |

---

## Technical Architecture

### Deployment Model
- Single Azure App Service (B1 or higher)
- Azure Table Storage account
- Entra ID app registration

### Project Structure
```
SignalYard/
├── SignalYard.Core/           # Entities, models, services
│   ├── Entities/
│   │   ├── LogEntry.cs
│   │   ├── Application.cs
│   │   └── ApiKeyLookup.cs
│   ├── Models/
│   │   ├── IngestModels.cs
│   │   └── QueryModels.cs
│   └── Services/
│       ├── ApiKeyService.cs
│       ├── LogStorageService.cs
│       └── ApplicationStorageService.cs
└── SignalYard.Web/            # Blazor Server application
    ├── Components/
    │   ├── Pages/
    │   │   ├── Home.razor          # Log viewer
    │   │   └── Applications.razor  # App management
    │   └── Layout/
    ├── Endpoints/
    │   └── IngestEndpoint.cs
    ├── Auth/
    │   └── ApiKeyAuthenticationHandler.cs
    └── Services/
        └── RetentionCleanupService.cs
```

### Table Storage Tables
1. **Logs** - Log entries
2. **Applications** - Application configurations
3. **ApiKeys** - API key lookup (hash → application name)

### Technology Stack
- .NET 9
- Blazor Server (interactive UI)
- Azure.Data.Tables SDK
- Microsoft.Identity.Web (Entra ID)
- Minimal APIs (ingestion endpoints)

---

## User Interface

### Log Viewer (Home Page)

**Toolbar**
- Application dropdown (required)
- Date/time range pickers
- Log level filter dropdown
- Search button

**Search Box** (appears after query)
- Text input for client-side filtering
- Result count indicator

**Log Entry List**
- Compact single-line view showing: level badge, timestamp, message
- Click to expand showing: message template, properties (JSON), exception
- Colour-coded by level (error/fatal highlighted)
- Newest entries first

**Visual Design**
- Dark theme (reduces eye strain for log viewing)
- Monospace font for log content
- Level indicators: VRB, DBG, INF, WRN, ERR, FTL

### Applications Page

**Header**
- Page title
- "Add Application" button

**Application Table**
| Column | Content |
|--------|---------|
| Name | App name + description |
| API Key | Prefix with ellipsis |
| Status | Enabled/Disabled badge |
| Retention | Days |
| Created | Date |
| Actions | Edit, Regenerate Key, Delete |

**Create/Edit Dialog**
- Name (read-only on edit)
- Description
- Retention days
- Enabled checkbox (edit only)

**API Key Display**
- Shown once on create or regenerate
- Warning that it won't be shown again
- Copy-friendly format

---

## Integration

### Serilog Configuration

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.Http(
        requestUri: "https://your-signalyard.azurewebsites.net/api/events/raw",
        queueLimitBytes: null,
        httpClient: new SignalYardHttpClient("sy_your_api_key"))
    .CreateLogger();
```

**Custom HttpClient for API Key**
```csharp
public class SignalYardHttpClient : IHttpClient
{
    private readonly HttpClient _client;
    
    public SignalYardHttpClient(string apiKey)
    {
        _client = new HttpClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }
    
    // ... implement IHttpClient
}
```

### Alternative: Direct HTTP Sink
```csharp
.WriteTo.Http(
    requestUri: "https://your-signalyard.azurewebsites.net/api/ingest",
    textFormatter: new CompactJsonFormatter(),
    httpClient: new SignalYardHttpClient("sy_your_api_key"))
```

---

## Cost Considerations

### Azure Table Storage Pricing (approximate)
- Storage: ~$0.045/GB/month
- Transactions: ~$0.00036 per 10,000 transactions

### Example Costs
| Scenario | Monthly Storage | Transactions | Estimated Cost |
|----------|-----------------|--------------|----------------|
| Light (1M events, 500MB) | $0.02 | ~$0.04 | < $1 |
| Medium (10M events, 5GB) | $0.23 | ~$0.40 | < $2 |
| Heavy (100M events, 50GB) | $2.25 | ~$4.00 | < $10 |

### App Service
- B1 tier: ~$13/month (sufficient for most workloads)

---

## Security Considerations

1. **API Keys**
   - Generated with cryptographic randomness
   - Stored as SHA256 hashes only
   - Prefixed for easy identification
   - Can be regenerated (invalidates old key)

2. **UI Access**
   - Entra ID authentication required
   - Tenant-restricted access

3. **Data Isolation**
   - Logs partitioned by application
   - API keys scoped to single application

4. **Transport**
   - HTTPS enforced
   - App Service handles TLS termination

---

## Future Considerations

These are explicitly out of scope for v1 but may be considered later:

- Real-time log streaming via SignalR
- Log level alerting (email/webhook on errors)
- Dashboard with log volume metrics
- Export to blob storage for long-term archive
- Multiple API keys per application
- Role-based access (viewer vs admin)

---

## Success Metrics

1. **Functional**: Successfully ingest and query logs from 3+ applications
2. **Performance**: Query 1 hour of logs (up to 10,000 entries) in < 2 seconds
3. **Cost**: Monthly storage + compute under $20 for typical workloads
4. **Reliability**: 99.9% ingestion success rate

---

## Open Questions

1. Should there be a maximum batch size for ingestion requests?
2. Should we support log level minimum filtering at the application level?
3. Is 1000 the right default limit for query results?
4. Should retention cleanup run at a configurable time of day?