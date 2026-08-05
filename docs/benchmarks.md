# Benchmarks

## Dogfood validation

Top 3 evidences — end-to-end Cursor workflows on a local LLM (Qwen-35B behind Comprexy OSS):

### 1. Dashboard implementation + tests (125 turns)

Continued `apps/dashboard/` (layout, chart polish; commit `5ca87ca`). About 10.35M baseline tokens → 5.19M sent-equivalent; after ~175k compression overhead, rollup net savings ~4.99M (48.24%). After working-memory folds, actual prompts stayed roughly 15–60k (within the 64k soft limit used in this setup). Final turn ~124k → ~55k estimated tokens (247 raw → 76 sent; WM v3). Parent-session telemetry only (subagents not included).

Evidence: [docs/evidence/5ca87ca.md](evidence/5ca87ca.md) ([dashboard snapshot](evidence/5ca87ca.png))

### 2. Earlier implementation (331 turns)

Built `apps/dashboard/` in one conversation (commit `721ea29`). About 66.05M baseline tokens → 10.21M sent-equivalent; after 7.47M compression overhead, rollup net savings ~48.37M (73.23%). After the first working-memory fold, actual prompts stayed mostly ~20–50k. Final analysis (last turn under 256k baseline): ~256k → ~35k estimated tokens.

Evidence: [docs/evidence/721ea29.md](evidence/721ea29.md)

### 3. Planning (29 turns)

Authored dashboard planning docs in a 29-turn workflow (commit `d2e0faa`). About 2.00M baseline tokens across the run → ~1.08M sent-equivalent (~800k saved). Final turn ~94k → ~37k estimated tokens (77 raw → 31 sent); effective prompts stayed roughly 21–58k.

Evidence: [docs/evidence/d2e0faa.md](evidence/d2e0faa.md)

These are dogfood workflows and a single local benchmark, not universal benchmarks — and they do not claim measured tok/s gains.

## Benchmark comparison

A structured two-arm benchmark compared MAF client-side compaction (`ToolSchema:Off`, 256k context window) against Comprexy Virtual Tools (`ToolSchema:Virtual`, 64k soft limit) on the same six prompt lists (123 total prompts) using Qwen-35B.

Across three fully completed conversations, Comprexy saved 4,421,674 tokens (30.8%) versus the baseline — 37.4% on heavy-tool-usage, 26.5% on mixed-workload, 28.9% on smoke-large-blob. The remaining three conversations failed on the baseline arm at the 256k context limit (HTTP 502); Comprexy completed one additional prompt beyond each baseline failure (harness default `survived_baseline_failure` margin).

Evidence: [docs/evidence/65f1b1b.md](evidence/65f1b1b.md)
