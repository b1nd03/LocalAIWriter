# Benchmarks

LocalAI Writer includes automated test coverage for grammar correction quality, false-positive control, and meta-output rejection. The benchmark values below should be refreshed before each public release on the target machine and selected model.

## Model Comparison

| Engine | Test Set | Target | Current Status | Notes |
| --- | --- | ---: | --- | --- |
| `qwen2.5:3b` through Ollama | BasicGrammar50 | Record before release | Pending run | Recommended balanced default for local correction. |
| `gemma2:2b` through Ollama | BasicGrammar50 | Record before release | Pending run | Lightweight option for faster local machines. |
| Rule-based fallback | BasicGrammar50 | Baseline only | Available | Handles simple punctuation, spacing, and common typo cleanup. |

## Regression Gates

The test suite contains release-oriented checks for:

- Advanced grammar correction accuracy.
- Clean-text false-positive avoidance.
- Rejection of explanatory or meta output when only corrected text is expected.
- Resilience around malformed text, unsafe paths, and local runtime failures.

Run:

```powershell
dotnet test LocalAIWriter.sln -c Release
```

## Benchmark Method

1. Start Ollama.
2. Pull the model under test, for example `ollama pull qwen2.5:3b`.
3. Select the model in LocalAI Writer settings.
4. Run the benchmark or test suite.
5. Record correction quality, false-positive rate, and average latency.

## Notes

Small local models are useful for privacy and speed, but quality depends on the selected model, hardware, prompt behavior, and text domain. Keep benchmark results tied to the exact model name and version whenever possible.
