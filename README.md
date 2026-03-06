# LocalAI Writer

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)]()

**LocalAI Writer** is a fast, offline, privacy-first grammar correction tool for Windows. It runs completely locally using [Ollama](https://ollama.com/), allowing you to fix grammar, spelling, and phrasing in any application just by selecting text and pressing a hotkey.

No cloud APIs. No subscriptions. Complete privacy.

## Features

- **Global Hotkey (Ctrl+Alt+G):** Select text in any app (Word, browser, Discord) and press the hotkey. The corrected text will be automatically pasted back.
- **100% Offline & Private:** Powered by local LLMs via Ollama. Your text never leaves your machine.
- **Instant Local UI:** Includes a standalone window for drafting and correcting text manually if you prefer not to use the hotkey.
- **Rule-based Fallback:** Fast, offline regex rules for basic punctuation and spacing improvements if the AI is offline.
- **Customizable Models:** Works with any Ollama model, including lightweight thinking models like `qwen2.5` or `gemma2`.

## Prerequisites

1. **Windows 10 / 11** (x64)
2. **[Ollama](https://ollama.com/)** installed and running.
3. At least one model pulled in Ollama (e.g., `ollama pull qwen2.5:3b`).

## Installation

### Option 1: Pre-compiled binaries (Coming Soon)
Download the latest `.exe` from the [Releases](https://github.com/b1nd03/LocalAIWriter/releases) tab. It is a single self-contained file, no installation required.

### Option 2: Build from Source
1. Clone this repository:
   ```bash
   git clone https://github.com/b1nd03/LocalAIWriter.git
   cd LocalAIWriter
   ```
2. Build the project using the .NET 8 SDK:
   ```bash
   dotnet publish src/LocalAIWriter -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./publish
   ```
3. Run `LocalAIWriter.exe` from the `publish` folder.

## How to Use

1. **Start Ollama** in the background.
2. Run **LocalAI Writer**. You'll see a small icon in your system tray.
3. Open the **Settings** (via tray icon or main window) and select your preferred Ollama model.
4. **Usage:**
   - Select any text anywhere on your computer.
   - Press **Ctrl+Alt+G**.
   - A tray notification will appear. After a few seconds, the corrected text will replace your selection.

## Configuration

Settings are saved automatically to `%APPDATA%\LocalAIWriter\settings.json`. You can configure:
- **Ollama Endpoint:** Defaults to `http://localhost:11434`.
- **Model:** Auto-detects installed models.
- **Theme:** Light/Dark/System.
- **High Contrast Mode:** Supported for accessibility.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
