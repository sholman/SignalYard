# SignalYard

A lightweight, self-hosted structured logging tool designed to run in Azure App Service with Azure Table Storage as the backend.

## Features

- 🔍 **Log Viewer** - Query and search logs with a dark-themed UI
- 📱 **Application Management** - Create and manage applications with API keys
- 🔐 **Secure** - Entra ID authentication for UI, API keys for ingestion
- 💰 **Cost-effective** - Uses Azure Table Storage (pennies/month)
- 🧹 **Auto-cleanup** - Configurable retention with automatic partition deletion

## Prerequisites

- .NET 9 SDK
- Azure Storage Account (or Azurite for local development)
- Azure Entra ID app registration (for production)

## Local Development

### 1. Start Azurite (Azure Storage Emulator)

```bash
# Using npm
npm install -g azurite
azurite --silent --location ./azurite --debug ./azurite/debug.log

# Or using Docker
docker run -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite
```

### 2. Configure the Application

For local development, the app uses `UseDevelopmentStorage=true` by default (see `appsettings.Development.json`).

For Entra ID authentication in development, you can either:
- Set up an Entra ID app registration and update `appsettings.Development.json`
- Temporarily disable authentication for testing

### 3. Run the Application

```bash
cd SignalYard.Web
dotnet run
```

Navigate to `https://localhost:5001` (or the URL shown in the console).

## Azure Deployment

### 1. Create Azure Resources

```bash
# Create resource group
az group create --name rg-signalyard --location australiaeast

# Create storage account
az storage account create \
  --name stsignalyard \
  --resource-group rg-signalyard \
  --location australiaeast \
  --sku Standard_LRS

# Create App Service Plan
az appservice plan create \
  --name asp-signalyard \
  --resource-group rg-signalyard \
  --sku B1 \
  --is-linux

# Create Web App
az webapp create \
  --name app-signalyard \
  --resource-group rg-signalyard \
  --plan asp-signalyard \
  --runtime "DOTNETCORE:9.0"
```

### 2. Configure Entra ID

1. Go to Azure Portal → Entra ID → App registrations
2. Create a new registration:
   - Name: `SignalYard`
   - Supported account types: Single tenant (or as needed)
   - Redirect URI: `https://your-app.azurewebsites.net/signin-oidc`
3. Note the Application (client) ID and Directory (tenant) ID
4. Under Authentication, add the redirect URI for your app

### 3. Configure App Settings

```bash
# Get storage connection string
CONN_STRING=$(az storage account show-connection-string \
  --name stsignalyard \
  --resource-group rg-signalyard \
  --query connectionString -o tsv)

# Set configuration
az webapp config appsettings set \
  --name app-signalyard \
  --resource-group rg-signalyard \
  --settings \
    "ConnectionStrings__TableStorage=$CONN_STRING" \
    "AzureAd__TenantId=YOUR_TENANT_ID" \
    "AzureAd__ClientId=YOUR_CLIENT_ID"
```

### 4. Deploy

```bash
dotnet publish -c Release
az webapp deploy --resource-group rg-signalyard --name app-signalyard --src-path ./publish.zip
```

## Using SignalYard

### 1. Create an Application

1. Navigate to the Applications page
2. Click "Add Application"
3. Enter a name and description
4. Copy the generated API key (shown only once!)

### 2. Configure Serilog

Install the SignalYard sink:

```bash
dotnet add package Serilog.Sinks.SignalYard
```

Configure your application — pass the SignalYard server URL and your API key:

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.SignalYard(
        serverUrl: "https://your-signalyard.azurewebsites.net",
        apiKey: "sy_your_api_key_here")
    .CreateLogger();

Log.Information("User {Username} logged in from {IpAddress}", "john.doe", "10.0.0.1");

Log.CloseAndFlush(); // flush buffered events on shutdown
```

The sink formats events as CLEF, batches them, and posts to `/api/events/raw` with the
`X-Api-Key` header for you. See [Serilog.Sinks.SignalYard](https://github.com/simonholman/Serilog.Sinks.SignalYard)
for configuration options (batch size, flush period, minimum level).

### 3. Query Logs

1. Navigate to the Log Viewer (home page)
2. Select an application
3. Choose a date range
4. Optionally filter by log level
5. Click Search
6. Use the search box to filter results client-side

## MCP Endpoint (AI log investigation)

SignalYard exposes a [Model Context Protocol](https://modelcontextprotocol.io) server at `/mcp` so AI
tools such as Claude Code and VS Code can investigate logs across **all** applications. It is
read-only and gated by a single global "investigator" API key — separate from the per-application
`sy_` ingestion keys.

### 1. Set the investigator key

Provide a strong random secret via the `Mcp__ApiKey` environment variable (double underscore maps to
the `Mcp:ApiKey` configuration value). Leaving it unset **disables** the endpoint (every request is
rejected).

```bash
# Azure App Service
az webapp config appsettings set \
  --name app-signalyard \
  --resource-group rg-signalyard \
  --settings "Mcp__ApiKey=$(openssl rand -hex 32)"
```

For local development, set it in user-secrets or `appsettings.Development.json`:

```json
{ "Mcp": { "ApiKey": "dev-mcp-key" } }
```

### 2. Connect a client

The key is sent in the `X-Api-Key` header (an `Authorization: Bearer <key>` header also works).

**Claude Code:**

```bash
claude mcp add --transport http signalyard https://your-signalyard.azurewebsites.net/mcp \
  --header "X-Api-Key: <your-mcp-key>"
```

**VS Code** (`.vscode/mcp.json`):

```json
{
  "servers": {
    "signalyard": {
      "type": "http",
      "url": "https://your-signalyard.azurewebsites.net/mcp",
      "headers": { "X-Api-Key": "<your-mcp-key>" }
    }
  }
}
```

### 3. Available tools

| Tool | Description |
|------|-------------|
| `list_applications` | List all applications (name, description, enabled, retention). |
| `query_logs` | Query log entries for one or all applications over a time range. Params: `application`, `level`, `from`, `to`, `maxResults` (default 200), `search`, `includeProperties`. Defaults to the last 24 hours. |
| `get_log_stats` | Aggregate counts by level, over time, and per application. Params: `application`, `from`, `to`, `bucketMinutes`. |

> `search` is a post-filter over the returned page (up to `maxResults`), not a full-range search —
> Table Storage has no server-side text search. Narrow the time range for exhaustive results.

## Configuration

| Setting | Description | Default |
|---------|-------------|---------|
| `ConnectionStrings:TableStorage` | Azure Storage connection string | - |
| `TableStorage:AccountName` | Storage account name (for managed identity) | - |
| `Mcp:ApiKey` | Global key for the `/mcp` investigation endpoint (unset = disabled) | - |
| `AzureAd:TenantId` | Entra ID tenant ID | - |
| `AzureAd:ClientId` | Entra ID application ID | - |
| `RetentionCleanup:StartupDelayMinutes` | Delay before first cleanup run | 60 |
| `RetentionCleanup:IntervalHours` | Hours between cleanup runs | 24 |

## Architecture

```
SignalYard/
├── SignalYard.Core/           # Domain layer
│   ├── Entities/           # Table Storage entities
│   ├── Models/             # DTOs and request/response models
│   └── Services/           # Business logic services
└── SignalYard.Web/            # Blazor Server application
    ├── Auth/               # API key authentication
    ├── Components/         # Blazor pages and layouts
    ├── Endpoints/          # Minimal API endpoints
    └── Services/           # Background services
```

## Table Storage Schema

### Logs Table
- **PartitionKey**: `{ApplicationName}_{YearMonth}` (e.g., `MyApp_202501`)
- **RowKey**: `{InvertedTicks}_{Guid}` (newest first)

### Applications Table
- **PartitionKey**: `Application`
- **RowKey**: Application name

### ApiKeys Table
- **PartitionKey**: `ApiKey`
- **RowKey**: SHA256 hash of API key

## License

MIT
