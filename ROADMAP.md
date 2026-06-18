# Roadmap

LocalAI Writer is focused on fast local grammar correction with a privacy-first Windows workflow.

## Phase 1 - Stable Local Correction

- [x] Global hotkey correction flow.
- [x] Clipboard capture and paste-back behavior.
- [x] Ollama-backed local LLM correction.
- [x] Rule-based fallback for simple corrections.
- [x] Model, endpoint, theme, and accessibility settings.
- [x] Prompt hardening to reduce explanations and keep output correction-only.

## Phase 2 - Realtime Experience

- [x] Realtime stale-request cancellation.
- [x] Realtime deduplication for unchanged text.
- [x] Popup spam cooldown for repeated suggestions.
- [x] Stricter confidence floor in automatic mode.
- [ ] Popup auto-dismiss using `PopupTimeoutMs`.
- [ ] Per-app pacing for chat apps, editors, and document tools.
- [ ] Optional silent apply mode for tiny high-confidence fixes.

## Phase 3 - Grammar Engine Improvements

- [ ] Add a pluggable grammar engine interface.
- [ ] Keep `RuleBased + Ollama` as the default adapter.
- [ ] Add an optional token-level edit tagging engine.
- [ ] Add engine selection: `Auto`, `Ollama`, and `RuleBased`.
- [ ] Add benchmark reporting for accuracy, no-change precision, and latency.

## Phase 4 - Release Hardening

- [ ] Add signed installer builds.
- [ ] Add automatic release artifact publishing.
- [ ] Add local-only anonymized counters for latency and accepted suggestions.
- [ ] Expand regression tests from real failure cases without storing user text.
- [ ] Improve accessibility review for keyboard-only and high-contrast users.
