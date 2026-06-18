# Benchmarks

LocalAI Writer includes automated test coverage for grammar correction quality, false-positive control, and meta-output rejection. The benchmark values below were recorded on a local Windows machine with Ollama running.

## Model Comparison

| Engine | Test Set | Target | Current Status | Notes |
| --- | --- | ---: | --- | --- |
| `qwen2.5:3b` through Ollama | BasicGrammar50 | Exact-match correction | 43/50 exact, 86.0%; mean 2,984 ms, p50 2,598 ms, p95 2,651 ms | Includes first cold request. Good local default, but still misses some grammar cases. |
| `gemma2:2b` through Ollama | BasicGrammar50 | Exact-match correction | Not recorded; model pull timed out after 10 minutes on this machine | Keep as an optional lightweight candidate when the model is available locally. |
| Rule-based fallback | AdvancedGrammar100 | >= 90% | 100/100 exact, 100.0% | Verified by `AdvancedGrammarAccuracyBenchmarkTests`. |
| Rule-based fallback | NoChange100 | >= 95% unchanged | 100/100 unchanged, 100.0% | Verified by `NoChangeFalsePositiveBenchmarkTests`. |
| Safety validation | RealWorld50 | 100% rejection of analysis-style output | 50/50 rejected, 100.0% | Verified by `RealWorldMetaOutputRejectionTests`. |

## Regression Gates

The test suite contains release-oriented checks for:

- Advanced grammar correction accuracy.
- Clean-text false-positive avoidance.
- Rejection of explanatory or meta output when only corrected text is expected.
- Resilience around malformed text, unsafe paths, and local runtime failures.

Verified command:

```powershell
dotnet test LocalAIWriter.sln --configuration Release --no-build --filter "FullyQualifiedName~AdvancedGrammarAccuracyBenchmarkTests|FullyQualifiedName~NoChangeFalsePositiveBenchmarkTests|FullyQualifiedName~RealWorldMetaOutputRejectionTests" --logger "console;verbosity=detailed"
```

## Benchmark Method

1. Start Ollama.
2. Pull the model under test, for example `ollama pull qwen2.5:3b`.
3. Select the model in LocalAI Writer settings.
4. Run the benchmark or test suite.
5. Record correction quality, false-positive rate, and average latency.

## Latest Local Run

- Date: 2026-06-19
- Ollama version: `0.17.1`
- Model tested through Ollama: `qwen2.5:3b`
- BasicGrammar50 result: `43/50` exact matches, `86.0%`
- Latency: mean `2,984 ms`, p50 `2,598 ms`, p95 `2,651 ms`
- Rule-based regression gates: `AdvancedGrammar100 100%`, `NoChange100 100%`, `RealWorld50 safety rejection 100%`

## Notes

Small local models are useful for privacy and speed, but quality depends on the selected model, hardware, prompt behavior, and text domain. Keep benchmark results tied to the exact model name and version whenever possible.
