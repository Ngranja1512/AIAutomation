# OpenClawIntegration

A .NET 10 Worker Service that uses **GitHub Copilot** to research and summarise topics on a daily schedule, then delivers the results to your **email inbox**.

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
       │   ResearchService     │
       │  (orchestration)      │
       └──────────┬────────────┘
                  │
                  ▼
       ┌───────────────────────┐
       │   CopilotService      │◄── GitHub Copilot token
       │  (Copilot API)        │
       └──────────┬────────────┘
                  │ summaries
                  ▼
       ┌───────────────────────┐
       │   EmailService        │◄── SMTP credentials
       │  (SMTP delivery)      │
       └───────────────────────┘
```

## Quick start

### 1. Prerequisites

| Requirement | How to get it |
|---|---|
| .NET 10 SDK | https://dotnet.microsoft.com/download |
| GitHub personal access token (with Copilot access) | GitHub → Settings → Developer settings → Personal access tokens |
| SMTP email account | Any provider (e.g. Gmail with an app password) |

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
    "Email": {
      "SmtpHost": "smtp.gmail.com",
      "SmtpPort": 587,
      "Username": "you@gmail.com",
      "Password": "your_app_password",
      "FromAddress": "you@gmail.com",
      "ToAddress": "recipient@example.com"
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
| `EMAIL_SMTP_HOST` | SMTP server hostname |
| `EMAIL_SMTP_PORT` | SMTP port (e.g. `587`) |
| `EMAIL_USERNAME` | SMTP login username |
| `EMAIL_PASSWORD` | SMTP password or app password |
| `EMAIL_FROM_ADDRESS` | Sender email address |
| `EMAIL_TO_ADDRESS` | Recipient email address |

Optionally, add these **Repository Variables** (`Settings → Secrets and variables → Actions → Variables`):

| Variable | Default | Description |
|---|---|---|
| `SCHEDULER_CRON` | `0 8 * * *` | Cron expression (UTC) |
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
| `ApiUrl` | `https://models.inference.ai.azure.com` | Copilot API base URL |
| `Model` | `gpt-4o` | Model used for summarisation |
| `MaxTokens` | `1000` | Max tokens per summary response |

### `Email`

| Key | Description |
|---|---|
| `SmtpHost` | SMTP server hostname (e.g. `smtp.gmail.com`) |
| `SmtpPort` | SMTP port (e.g. `587` for STARTTLS) |
| `Username` | SMTP login username |
| `Password` | SMTP password or app-specific password |
| `FromAddress` | Sender email address |
| `ToAddress` | Recipient email address |

### `Scheduler`

| Key | Default | Description |
|---|---|---|
| `CronExpression` | `0 8 * * *` | 5-part cron expression (UTC). Used in daemon mode only; in GitHub Actions the schedule is controlled by the workflow file. |

### `Topics`

Array of topic objects:

| Key | Description |
|---|---|
| `Name` | Short display name (used in the email subject/body) |
| `Description` | Context provided to Copilot for richer summaries |

## Project structure

```
OpenClawIntegration/
├── src/OpenClawIntegration/
│   ├── Models/
│   │   ├── AppSettings.cs        # Configuration models
│   │   ├── ApiModels.cs          # Copilot API DTOs
│   │   ├── Topic.cs              # Topic to research
│   │   └── SummaryResult.cs      # Result of a summary run
│   ├── Services/
│   │   ├── CopilotService.cs     # GitHub Copilot chat-completions
│   │   ├── ResearchService.cs    # Orchestrates topic research via Copilot
│   │   └── EmailService.cs       # SMTP email delivery
│   ├── Workers/
│   │   ├── SummaryWorker.cs      # Cron-scheduled background worker
│   │   └── OneShotWorker.cs      # One-shot execution (--run-once)
│   ├── Program.cs
│   ├── appsettings.json          # Config (gitignored – no secrets here)
│   └── appsettings.example.json  # Example config template
├── tests/OpenClawIntegration.Tests/
│   └── Services/
│       ├── CopilotServiceTests.cs
│       ├── ResearchServiceTests.cs
│       └── SummaryWorkerTests.cs
├── .github/workflows/
│   ├── scheduled-summary.yml     # Daily cron + manual trigger
│   └── ci.yml                    # Build & test on push/PR
└── OpenClawIntegration.slnx
```

## Running tests

```bash
dotnet test
```
