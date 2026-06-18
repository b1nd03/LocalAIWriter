# Privacy

LocalAI Writer is designed for local-first writing correction.

## Local Processing

- The app can run fully offline after Ollama and a local model are installed.
- No cloud API key is required.
- Selected text is sent to the configured Ollama endpoint, which defaults to `http://localhost:11434`.
- The app does not upload user text to a remote service.
- Rule-based fallback corrections run inside the app process.

## Local Settings

Settings are stored locally at:

```text
%APPDATA%\LocalAIWriter\settings.json
```

This file may include the configured Ollama endpoint, selected model, theme, hotkey behavior, accessibility settings, and correction preferences.

## User Responsibility

LocalAI Writer depends on the behavior of the model the user installs. Different local models can produce different correction quality, tone, and latency. Users should review corrected text before using it in sensitive, legal, medical, academic, or professional contexts.

## Network Notes

The default setup uses a local Ollama server. If the Ollama endpoint is changed to a remote URL, selected text is sent to that configured endpoint. Only use remote endpoints you control and trust.
