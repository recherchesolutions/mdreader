# Implementation plan — docs/recommendations.md

Branch: `feat/recommendations`. Each phase is independently verifiable and
committed separately. Cross-cutting rules: no new network calls, security model
unchanged, byte-faithful IO preserved, `dotnet format` + tests green per phase.

## Phase 1 — Editing safety and recovery (rec. D)
- `AtomicFileWriter` in Core: same-directory temp file + `File.Replace`
  (fallback move for new files), preserving bytes from `TextFileIO`; cleanup on
  failure. Fault-oriented unit tests.
- `RecoveryStore` in App: debounced, atomic, bounded crash-recovery snapshots of
  dirty buffers under `%APPDATA%\mdreader\recovery\`; offered on next launch
  (restore / open original / discard); cleared on save/close. Unit tests on the
  store logic.
- Close review: one dialog listing all dirty documents with save / discard /
  cancel (replaces sequential per-tab prompts).

## Phase 2 — Navigation (rec. B)
- `NavigationHistory` model (per tab, bounded, back/forward, no scroll noise) +
  unit tests. Jump sources: TOC clicks, in-document anchors, Ctrl+G, relative
  .md links (which already open tabs — record the jump origin).
- Alt+Left / Alt+Right + toolbar buttons.
- `Ctrl+G`: compact dialog accepting a line number or heading filter text;
  Enter jumps, Esc cancels; empty input = no-op.
- `ScrollPositionStore`: per-file last reader/editor line, bounded (200 files,
  LRU), versioned JSON in appdata; restore on open; prune missing files. Unit
  tests.
- TOC: keyboard navigation (arrow keys + Enter within the rail), focus command
  via Ctrl+Shift+O second press.

## Phase 3 — Reading information (rec. C)
- `ReadingStats` in Core: word count (Unicode-aware; CJK counted per character),
  estimated reading time (238 wpm prose default, documented), code-block-aware.
  Pure + unit tested.
- Status bar: words · minutes · progress % (from existing throttled
  scrollChanged messages; no new WebView traffic). Screen-reader-friendly
  automation names; collapses gracefully when narrow.

## Phase 4 — Interface refinement (rec. G)
- Compact command bar: Open, Back/Forward, Reader/Source/Split, TOC, Find,
  Export. Native restrained styling, light/dark aware, tooltips + access keys.
- Dirty-state dot on tabs (visual, not just text), hover/focus states.

## Phase 5 — Deterministic blocking UI suite (rec. E)
- Split integration tests: `[Trait("suite","blocking")]` deterministic set
  (open/render, external reload, single-instance handoff, headless HTML+PDF
  export, version) driven by the log-based harness with bounded readiness waits;
  FlaUI remains non-blocking. CI: blocking job gates, uploads logs on failure.
- Editing/save keystroke flows stay in the non-blocking FlaUI suite (documented
  limitation: cannot type into Monaco deterministically on hosted runners).

## Phase 6 — Performance budgets (rec. A)
- Benchmarks: 100KB / representative 1MB / 10MB generated docs; separate parse
  vs sanitize vs serialize phases; budget file `benchmarks/budgets.json`.
- `--check` mode exits non-zero when a deterministic budget regresses (2x
  headroom over baseline to avoid flakiness). CI job. Cold-start measured by
  script (`tools/measure-startup.ps1`) and reported, not gated (environment-
  sensitive). Docs on running/interpreting.

## Phase 7 — Distribution size (rec. F)
- Baseline table (installer / portable / CLI). Add framework-dependent CLI
  variant; attempt Native AOT CLI (AngleSharp/YamlDotNet reflection audit);
  keep self-contained artifacts as-is. Release workflow additions + smoke test
  script per variant. Documented tradeoffs.

## Phase 8 — Documentation (rec. H)
- README: winget full identifier, screenshots (reader/source/split at 1280x800),
  comparison table, keyboard table (Ctrl+G, Alt+arrows), content-width text
  already reconciled. CHANGELOG Unreleased section. UTF-8 checks.

## Phase 9 — Final verification
- format check, Release build, all unit + blocking suites, benchmarks --check,
  packaging smoke, final diff review; report per the brief.

## Phase 7 findings (2026-08-02)

- Baseline sizes: installer 48.8 MB, portable 66.1 MB, self-contained CLI 72.4 MB.
- Native AOT CLI: **blocked on this machine** - ilcompiler needs the MSVC
  platform linker (Desktop C++ workload). Smallest follow-up: run
  `dotnet publish -p:PublishAot=true` on a runner with VS Build Tools and
  audit AngleSharp/YamlDotNet trim warnings.
- Framework-dependent single-file CLI measured 36.8 MB compressed, which is
  suspiciously self-contained-sized; needs a clean-obj investigation before
  shipping as a release variant. Not published yet - existing artifacts are
  unchanged.
