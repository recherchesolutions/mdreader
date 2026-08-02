# Claude Code prompt: implement the mdreader recommendations

You are working in the `mdreader` repository, a Windows-first WPF application
for reading Markdown with optional source editing. Implement every actionable
recommendation in `docs/recommendations.md` while preserving the product's core
positioning:

> A fast, beautiful Markdown reader for Windows that happens to edit.

This is not a request to add AI, sync, vaults, backlinks, WYSIWYG editing, or a
plugin ecosystem. Do not add those features. Keep the interface calm,
reader-first, offline-first, and native to Windows.

## Working rules

1. Read `README.md`, `CONTRIBUTING.md`, `SECURITY.md`, `CHANGELOG.md`,
   `docs/recommendations.md`, the project files, and the relevant implementation
   and tests before changing code.
2. Inspect `git status` and all existing diffs first. The worktree may contain
   user changes, including ongoing MSIX/package work. Preserve them and do not
   revert, overwrite, reformat, or commit unrelated changes.
3. Create a written implementation plan grouped into small, independently
   verifiable phases. Then implement the complete plan. Do not stop after the
   plan or after the first feature.
4. Prefer focused changes that fit the existing architecture. Do not perform a
   broad framework migration or rewrite the application.
5. Maintain the security model: documents are untrusted, raw HTML remains
   sanitized, remote content stays blocked by default, CSP protections remain
   intact, and navigation must be intercepted safely.
6. Preserve byte-faithful behavior for encoding, BOM, and line endings.
7. Preserve existing keyboard shortcuts and add new shortcuts without conflicts.
8. Add or update automated tests for every behavior that can be tested reliably.
9. Do not declare success until the solution builds, blocking tests pass, and
   the relevant UI flows have been exercised or their verification limitations
   are documented.
10. Do not commit or push unless explicitly instructed to do so.

## Required implementation

### A. Performance budgets

- Extend the existing benchmark project to measure at least:
  - Markdown pipeline/render time for small, representative, and 10 MB files.
  - HTML assembly/sanitization time.
  - Application cold-start time to first readable document where this can be
    measured robustly.
- Add stable representative fixtures without checking in unnecessarily huge
  generated files. Generate large deterministic input during the benchmark or
  test when practical.
- Establish documented performance budgets based on a baseline measurement from
  this machine. Avoid brittle nanosecond-level assertions; use sensible
  regression thresholds.
- Add a CI job or script that can enforce the deterministic budgets. If true GUI
  cold-start timing is too environment-sensitive for a hard gate, report it
  separately while gating deterministic rendering benchmarks.
- Document how to run and interpret the benchmarks locally.

### B. Persistent document navigation

- Add a collapsible table-of-contents sidebar integrated into the main document
  view. It must work in reader and split modes, clearly indicate the current
  section, support keyboard navigation, and remain usable at narrow widths.
- Preserve the existing TOC command and `Ctrl+Shift+O`; make it toggle/focus the
  integrated sidebar as appropriate.
- Add `Ctrl+G` navigation. Provide a compact dialog or command UI that accepts a
  source line number or filters/selects a heading. Define intuitive parsing,
  validation, empty-state, and cancellation behavior.
- Add per-tab back and forward navigation for internal document jumps such as
  headings, TOC selections, and relative Markdown-document links. Add commands,
  tooltips, and conventional shortcuts where they do not conflict. Do not place
  normal scrolling into history.
- Persist each file/tab's last reader and editor scroll position across
  application restarts. Bound and prune stored history, handle moved/deleted
  files gracefully, and avoid storing document contents.
- Add unit tests for navigation state/history and persistence logic. Add focused
  integration coverage for TOC selection, `Ctrl+G`, history, and restoration.

### C. Reading information

- Add unobtrusive status-bar information for word count, reading progress, and
  estimated reading time. Include the current source line or section when it is
  reliable.
- Define and document the word-count and reading-time calculation. Use a
  reasonable default reading speed and handle empty, very large, CJK, and
  code-heavy documents sensibly.
- Update information asynchronously or incrementally so typing and scrolling
  stay responsive. Avoid excessive WebView-to-host messages.
- Ensure status text remains understandable to screen readers and does not
  crowd small windows.
- Unit-test the pure calculation and formatting logic.

### D. Editing safety and recovery

- Add visible dirty markers to modified tabs and ensure the window title/state
  remains accurate.
- When closing a tab or the application, present a clear review of all unsaved
  documents and allow users to save selected documents, discard selected
  changes, or cancel closing. Never silently discard edits.
- Implement crash recovery for unsaved buffers:
  - Store recovery data in the per-user application-data directory.
  - Write it atomically and debounce writes while editing.
  - Do not overwrite the original document automatically.
  - On the next launch, clearly offer recovery, comparison/context, or discard.
  - Remove stale recovery data after successful saves or explicit discard.
  - Bound storage and clean abandoned entries safely.
- Make normal file saves atomic while preserving encoding, BOM, and line
  endings. Use a same-directory temporary file and safe replacement strategy,
  retain useful file metadata where feasible, clean temporary files on failure,
  and surface actionable errors. Account for new files, locked files, FAT or
  network-volume behavior, and platforms where replacement semantics differ.
- Add fault-oriented tests for recovery and atomic saving, including interrupted
  writes, stale entries, cleanup, file locks, and round-trip fidelity.

### E. Deterministic blocking UI smoke tests

- Keep broad WebView2/FlaUI tests non-blocking if they remain inherently flaky,
  but extract or add a small deterministic suite that blocks CI.
- The blocking suite must cover the critical path: open, render, edit, save,
  external reload, and export.
- Improve the test harness rather than using arbitrary sleeps. Wait for explicit
  application/UI readiness conditions with bounded timeouts and useful failure
  diagnostics.
- Capture logs and screenshots on failure as CI artifacts.
- Quarantine only tests demonstrated to be flaky, with a comment explaining why
  and an issue/reference describing the exit criterion.

### F. Distribution size investigation and improvements

- Measure the current installed, portable, and CLI sizes and record a baseline.
- Investigate and implement safe size reductions, including:
  - A framework-dependent CLI distribution.
  - A Native AOT CLI build if all dependencies and behavior are compatible.
  - Trimming only where verified safe for WPF, WebView2, reflection, export, and
    Markdown extensions.
- Do not replace the existing self-contained artifacts unless the alternative
  is demonstrably reliable. It is acceptable to publish additional variants and
  explain the tradeoffs.
- Add automated smoke tests for every published variant and document size,
  prerequisites, startup behavior, and compatibility.
- Ensure release checksums and installer/portable workflows remain correct.

### G. Interface refinement

- Refine the surrounding application chrome while retaining a native,
  restrained Windows appearance.
- Add a compact command bar for common actions, including open, TOC/navigation,
  reader/source/split switching, find, and export where space permits.
- Improve active-tab, hover, focus, and dirty-state visuals in light and dark
  themes.
- Integrate the TOC control visually with the document area.
- Ensure controls reflow or collapse gracefully at narrow sizes. Do not reduce
  the reading area unnecessarily or duplicate every menu command permanently.
- Preserve full menu access, keyboard access, visible focus, high-contrast
  compatibility, screen-reader names, and WCAG AA contrast.
- Follow the existing system/custom theme behavior rather than introducing a
  separate design system.

### H. Documentation corrections and additions

- Reconcile the README's content-width description with the actual default
  behavior. Treat the implementation as authoritative unless it is clearly a
  bug, and update tests/settings labels as needed for consistency.
- Document the full winget identifier
  `recherchesolutions.mdreader`. Confirm whether the short command is reliable;
  use the unambiguous command as the primary installation instruction.
- Add current screenshots for reader, source, and split modes. Capture them from
  the implemented application at a consistent window size with representative,
  non-sensitive fixture content.
- Add a concise, factual comparison table covering mdreader, browser Markdown
  viewing, VS Code preview, and full note-taking applications. Avoid unsupported
  superiority claims and avoid naming competitors unnecessarily.
- Update the keyboard table, feature list, changelog under an `Unreleased`
  section, and any packaging documentation affected by this work.
- Keep all text files UTF-8 and fix any genuine mojibake encountered in files
  modified for this task.

## Cross-cutting acceptance criteria

- The reader remains the default and fastest path; new UI does not obscure the
  document or require setup.
- Opening a file, rendering it, toggling source/split mode, saving, live reload,
  print, HTML/PDF export, and rich-text copying continue to work.
- Multiple tabs and single-instance handoff continue to work.
- All new persistent state is versioned, bounded, tolerant of corrupt data, and
  stored under the existing application-data conventions.
- All user-facing failures are actionable and do not expose stack traces.
- No new network requests, telemetry, accounts, or background services are
  introduced.
- No security regression is introduced through WebView messages, links, local
  paths, recovery files, exported HTML, Mermaid, KaTeX, or raw HTML handling.
- New dependencies must be justified, permissively licensed, pinned, and added
  to third-party notices when required. Prefer the existing platform and
  dependencies.
- `dotnet format --verify-no-changes`, the Release build, unit tests, blocking UI
  smoke tests, packaging smoke tests, and relevant benchmarks pass.

## Suggested execution order

1. Baseline behavior, package sizes, performance, tests, and screenshots.
2. Extract testable services/models for navigation, reading statistics,
   persistence, recovery, and atomic saves.
3. Implement editing safety and recovery.
4. Implement TOC, `Ctrl+G`, history, and scroll restoration.
5. Add reading information and interface refinements.
6. Make the deterministic UI suite blocking and improve diagnostics.
7. Implement and verify safe distribution variants/size improvements.
8. Update screenshots and all documentation.
9. Run the complete verification matrix and review the final diff for scope,
   accessibility, security, performance, and accidental generated files.

## Final report

At completion, provide:

- A concise summary grouped by user-visible features, reliability, testing,
  packaging, and documentation.
- The exact commands run and their results.
- Before/after performance and package-size measurements.
- Any recommendation that could not be implemented, the concrete technical
  reason, evidence of attempted alternatives, and the smallest sensible
  follow-up. Do not silently omit requirements.
- A list of modified and newly created files.
- Remaining risks or manual checks, especially accessibility, WebView2 behavior,
  installer behavior, and screenshots on a real Windows desktop.
