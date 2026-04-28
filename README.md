# OpenClawIntegration

A .NET 10 Worker Service that integrates [OpenClaw AI](https://openclaw.ai/) with your **GitHub Copilot** account to research and summarise topics on a daily schedule, then deliver the results straight to your **WhatsApp**.

## How it works

```
┌─────────────────────────────────────────────┐
│  GitHub Actions (cron schedule)             │
│  or local daemon                            │
└──────────────────┬──────────────────────────┘
                   │ triggers
                   ▼
       ┌───────────────────────┐
       │   SummaryWorker       │
       │  (cron scheduler)     │
       └──────────┬────────────┘
                  │ for each topic
                  ▼
       ┌───────────────────────┐
       │   OpenClawService     │◄── OpenClaw API key
       │  (AI agent layer)     │    + GitHub Copilot token
       └──────────┬────────────┘
                  │ on UseDirectCopilot=true or empty ApiKey
                  ▼
       ┌───────────────────────┐
       │   CopilotService      │◄── GitHub Copilot token
       │  (Copilot API)        │
       └──────────┬────────────┘
                  │ summaries
                  ▼
       ┌───────────────────────┐
       │   WhatsAppService     │◄── Twilio credentials
       │  (Twilio API)         │
       └───────────────────────┘
```

## Quick start

### 1. Prerequisites

| Requirement | How to get it |
|---|---|
| .NET 10 SDK | https://dotnet.microsoft.com/download |
| GitHub personal access token (with Copilot access) | GitHub → Settings → Developer settings → Personal access tokens |
| OpenClaw API key | https://openclaw.ai/ (optional – set `UseDirectCopilot: true` to skip) |
| Twilio account + WhatsApp sender | https://www.twilio.com/whatsapp |

### 2. Configure secrets

Copy the example config and fill in your credentials:

```bash
cp src/OpenClawIntegration/appsettings.example.json src/OpenClawIntegration/appsettings.json
```

Edit `appsettings.json`:

```json
{
  "OpenClawIntegration": {
    "Copilot": {
      "Token": "ghp_YOUR_GITHUB_TOKEN",
      "Model": "gpt-4o"
    },
    "OpenClaw": {
      "ApiKey": "YOUR_OPENCLAW_KEY",
      "UseDirectCopilot": false
    },
    "WhatsApp": {
      "AccountSid": "AC...",
      "AuthToken": "your_twilio_auth_token",
      "FromNumber": "+14155238886",
      "ToNumber": "+YOUR_NUMBER"
    },
    "Scheduler": {
      "CronExpression": "0 8 * * *"
    },
    "Topics": [
      {
        "Name": "AI Industry News",
        "Description": "Latest developments in artificial intelligence and LLMs."
      },
      {
        "Name": "Tech Trends",
        "Description": "Emerging technologies and software engineering news."
      }
    ]
  }
}
```

> **Note:** `appsettings.json` is excluded from git via `.gitignore`. Never commit real credentials.

### 3. Run locally

**Daemon mode** (waits for the next cron tick, then repeats):
```bash
dotnet run --project src/OpenClawIntegration
```

**One-shot mode** (runs once immediately, then exits):
```bash
dotnet run --project src/OpenClawIntegration -- --run-once
```

## GitHub Actions scheduling

The repository includes two workflows:

| Workflow | File | Trigger |
|---|---|---|
| **Daily Summary** | `.github/workflows/scheduled-summary.yml` | Cron (daily 08:00 UTC) + manual |
| **CI** | `.github/workflows/ci.yml` | Push / pull request |

### Setting up the scheduled workflow

Add the following **Repository Secrets** (`Settings → Secrets and variables → Actions → Secrets`):

| Secret | Value |
|---|---|
| `COPILOT_TOKEN` | Your GitHub personal access token |
| `OPENCLAW_API_KEY` | Your OpenClaw API key |
| `TWILIO_ACCOUNT_SID` | Twilio Account SID |
| `TWILIO_AUTH_TOKEN` | Twilio Auth Token |
| `WHATSAPP_FROM_NUMBER` | Twilio WhatsApp sender number (E.164) |
| `WHATSAPP_TO_NUMBER` | Your WhatsApp number (E.164) |

Optionally, add these **Repository Variables** (`Settings → Secrets and variables → Actions → Variables`):

| Variable | Default | Description |
|---|---|---|
| `SCHEDULER_CRON` | `0 8 * * *` | Cron expression (UTC) |
| `OPENCLAW_USE_DIRECT_COPILOT` | `false` | Skip OpenClaw, use Copilot directly |
| `TOPIC_0_NAME` | `AI Industry News` | First topic name |
| `TOPIC_0_DESCRIPTION` | _(AI news)_ | First topic description |
| `TOPIC_1_NAME` | _(empty)_ | Second topic name |
| `TOPIC_1_DESCRIPTION` | _(empty)_ | Second topic description |

To change the schedule, edit the `cron` expression in `.github/workflows/scheduled-summary.yml`.

### Triggering manually

Go to **Actions → Daily Summary → Run workflow** to trigger immediately.

## Configuration reference

All settings live under the `OpenClawIntegration` key and can be overridden by environment variables using the standard .NET double-underscore separator convention (e.g. `OpenClawIntegration__Copilot__Token`).

### `Copilot`

| Key | Default | Description |
|---|---|---|
| `Token` | _(required)_ | GitHub personal access token with Copilot scope |
| `ApiUrl` | `https://api.githubcopilot.com` | Copilot API base URL |
| `Model` | `gpt-4o` | Model used for summarisation |
| `MaxTokens` | `1000` | Max tokens per summary response |

### `OpenClaw`

| Key | Default | Description |
|---|---|---|
| `ApiKey` | _(optional)_ | OpenClaw API key |
| `ApiUrl` | `https://api.openclaw.ai` | OpenClaw API base URL |
| `UseDirectCopilot` | `false` | When `true`, skip OpenClaw and call Copilot directly |

### `WhatsApp`

| Key | Description |
|---|---|
| `AccountSid` | Twilio Account SID |
| `AuthToken` | Twilio Auth Token |
| `FromNumber` | WhatsApp-enabled sender number in E.164 format |
| `ToNumber` | Recipient WhatsApp number in E.164 format |

### `Scheduler`

| Key | Default | Description |
|---|---|---|
| `CronExpression` | `0 8 * * *` | 5-part cron expression (UTC). Used in daemon mode only; in GitHub Actions the schedule is controlled by the workflow file. |

### `Topics`

Array of topic objects:

| Key | Description |
|---|---|
| `Name` | Short display name (used in the WhatsApp message header) |
| `Description` | Context provided to the AI for richer summaries |
| `Prompt` | _(optional)_ Full custom prompt that overrides the default |

## Project structure

```
OpenClawIntegration/
├── src/OpenClawIntegration/
│   ├── Models/
│   │   ├── AppSettings.cs        # Configuration models
│   │   ├── ApiModels.cs          # Copilot + OpenClaw API DTOs
│   │   ├── Topic.cs              # Topic to research
│   │   └── SummaryResult.cs      # Result of a summary run
│   ├── Services/
│   │   ├── CopilotService.cs     # GitHub Copilot chat-completions
│   │   ├── OpenClawService.cs    # OpenClaw agent orchestration
│   │   └── WhatsAppService.cs    # Twilio WhatsApp delivery
│   ├── Workers/
│   │   ├── SummaryWorker.cs      # Cron-scheduled background worker
│   │   └── OneShotWorker.cs      # One-shot execution (--run-once)
│   ├── Program.cs
│   ├── appsettings.json          # Config (gitignored – no secrets here)
│   └── appsettings.example.json  # Example config template
├── tests/OpenClawIntegration.Tests/
│   └── Services/
│       ├── CopilotServiceTests.cs
│       ├── OpenClawServiceTests.cs
│       └── SummaryWorkerTests.cs
├── .github/workflows/
│   ├── scheduled-summary.yml     # Daily cron + manual trigger
│   └── ci.yml                    # Build & test on push/PR
└── OpenClawIntegration.sln
```

## Running tests

```bash
dotnet test
```
