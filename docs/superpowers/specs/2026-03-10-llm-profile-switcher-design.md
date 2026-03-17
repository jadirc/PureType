# LLM Profile Quick-Switcher

## Problem

Users must manually re-enter API endpoint, API key, and model name every time they want to switch between different LLM providers (e.g., OpenAI, Anthropic, local Ollama). This is tedious and error-prone.

## Solution

Automatically save the last 10 LLM configurations as a most-recently-used (MRU) history list. A dropdown in the Settings dialog allows instant switching. Prompts remain global (not per-profile).

## Data Model

New record:

```csharp
public record LlmProfile(string BaseUrl, string ApiKey, string Model);
```

`LlmSettings` gains:

```csharp
public List<LlmProfile> RecentProfiles { get; set; } // max 10, MRU order
```

## Auto-Save Behavior

- When Settings dialog closes with OK and Endpoint + Key + Model are filled, the current combination is pushed to the front of `RecentProfiles`.
- Duplicates (matching BaseUrl + Model) are removed — the newer entry wins.
- List is capped at 10 entries.

## Display Name

Format: `"{Model} @ {ProviderName}"`

Provider name derived from URL:

| URL contains                           | Display name   |
| -------------------------------------- | -------------- |
| `api.openai.com`                       | OpenAI         |
| `api.anthropic.com`                    | Anthropic      |
| `openrouter.ai`                        | OpenRouter     |
| `generativelanguage.googleapis.com`    | Google Gemini  |
| Anything else                          | Hostname       |

## UI

A ComboBox placed at the top of the "AI Post-Processing" section in SettingsWindow, above the existing fields:

```
[v gpt-4o-mini @ OpenAI          ]   <-- Recent Profiles dropdown
-----------------------------------
API Endpoint:  [...]
API Key:       [...]
Model:         [...]
```

- Selecting a profile fills Endpoint, Key, and Model fields automatically.
- Fields remain fully editable after selection (the dropdown is a shortcut, not a lock).
- ComboBox only visible when `RecentProfiles` is non-empty.
- Tag attribute set to "profile recent model" for search filtering.

## Not In Scope

- No explicit profile naming or deletion
- No profile switching from MainWindow or via hotkey
- No per-profile prompts (prompts remain global)
