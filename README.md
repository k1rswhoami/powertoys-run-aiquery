# AI Query — PowerToys Run Plugin

A PowerToys Run plugin that lets you ask AI questions directly from the launcher.

## Features

- **Enter to send** — type your question, press Enter to submit (no accidental auto-fire)
- **Dual backend** — supports Claude (Anthropic) and any OpenAI-compatible API
- **History** — automatically saves Q&A pairs; browse with `?` or search with `?h keyword`
- **Follow-up** — use `?+` to ask a follow-up using the previous answer as context
- **Concise answers** — built-in system prompt instructs the model to be brief and direct

## Usage

| Input | Action |
|-------|--------|
| `? your question` | Ask a question (press Enter to send) |
| `?+ follow-up` | Ask a follow-up with previous answer as context |
| `?` (empty) | Browse recent history |
| `?h keyword` | Search history |

### Result actions

| Key | Action |
|-----|--------|
| Enter | Copy answer to clipboard |
| Ctrl+Enter | Open full answer in Notepad |
| Right-click history | Copy / Re-ask / Delete |

## Installation

### Requirements

- [PowerToys](https://github.com/microsoft/PowerToys) (latest)
- .NET 10 Runtime (included with PowerToys)

### Steps

1. Download the latest release `.zip`
2. Extract to `%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\AIQuery\`
3. Restart PowerToys
4. Open PowerToys Settings → PowerToys Run → AI Query → configure your API key

## Configuration

| Setting | Description |
|---------|-------------|
| AI Provider | `Claude (Anthropic)` or `OpenAI-Compatible` |
| API Key | Your API key |
| Base URL | Base URL for OpenAI-compatible APIs (e.g. `https://api.openai.com/v1`) |
| Model | Model name (e.g. `claude-sonnet-4-6`, `gpt-4o`) |
| Timeout | Request timeout in seconds (default: 15) |
| Global Mode | Show AI result for all queries without the `?` prefix |

## Building from Source

```powershell
# Requirements: .NET 10 SDK, PowerToys installed
git clone https://github.com/YOUR_USERNAME/powertoys-run-aiquery
cd powertoys-run-aiquery/Community.PowerToys.Run.Plugin.AIQuery
dotnet build -c Release

# Deploy
.\deploy.ps1
```

## License

MIT
