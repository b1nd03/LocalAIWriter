# Realtime Grammar Roadmap (Grammarly-Like)

## Goal
Deliver low-latency, low-false-positive, realtime grammar suggestions with safe apply behavior.

## Phase 1 - Stabilize Realtime (Implemented in this pass)
- [x] Prompt hardening for minimal grammar edits (no analysis/report output).
- [x] Correction-mode suppression for meta-evaluation inputs.
- [x] Realtime stale-request cancellation (latest typing pause wins).
- [x] Realtime dedupe to avoid re-processing unchanged text.
- [x] Popup spam cooldown to prevent repeated identical suggestions.
- [x] Stricter confidence floor in automatic mode.

## Phase 2 - Better Realtime UX
- [ ] Add popup auto-dismiss timer using `PopupTimeoutMs`.
- [ ] Add "Apply silently for tiny high-confidence fixes" option.
- [ ] Add per-app pacing (faster for chat apps, slower for docs/editors).
- [ ] Add lightweight telemetry counters (local-only): p50/p95 inference latency, suggestion accept rate.

## Phase 3 - GEC Engine (GECToR-style)
- [ ] Add pluggable grammar engine interface (`IGrammarCorrector`).
- [ ] Implement `RuleBased + Ollama` adapter (current default).
- [ ] Add `GecTaggerCorrector` adapter for token-level edit tagging.
- [ ] Add setting to select engine: `Auto`, `Ollama`, `GEC`.
- [ ] Benchmark gates:
  - AdvancedGrammar100 >= 90%
  - NoChange100 >= 95%
  - RealWorld50 meta-output rejection = 100%
  - Realtime p95 < 350ms on short text

## Phase 4 - Production Hardening
- [ ] Add canary rollout flag for new engine.
- [ ] Add fallback ladder: `GEC -> Ollama -> RuleBased`.
- [ ] Add regression suite from live failure logs (anonymized, no stored raw text).
