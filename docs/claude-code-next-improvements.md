# Claude Code prompt: verify, fix, and extend mdreader

You are working in the `mdreader` repository, a Windows-first WPF application
for reading Markdown with optional source editing. The first recommendation set
was substantially implemented. Your job now is to close the verified gaps and
then implement the highest-value improvements for normal users and developers.

Preserve the product's central promise:

> A fast, beautiful Markdown reader for Windows that happens to edit.

Do not add accounts, cloud sync, telemetry, AI features, vaults, backlinks,
WYSIWYG editing, or a general plugin ecosystem. Keep the application local,
offline-first, secure, lightweight in concept, and pleasant for non-technical
users.

## Before changing anything

1. Read `README.md`, `CONTRIBUTING.md`, `SECURITY.md`, `CHANGELOG.md`,
   `docs/recommendations.md`, `docs/implementation-plan-recommendations.md`,
   `docs/performance.md`, and the relevant source and tests.
2. Inspect `git status`, recent commits, and all existing diffs. Preserve user
   work and unrelated changes. Do not reset, revert, overwrite, or broadly
   reformat files you do not own.
3. Reproduce the current verification baseline:

   ```powershell
   dotnet format MdReader.slnx --verify-no-changes --no-restore
   dotnet build MdReader.slnx -c Release --no-restore
   dotnet test tests/MdReader.Core.Tests/MdReader.Core.Tests.csproj -c Release --no-build
   dotnet test tests/MdReader.Shell.Tests/MdReader.Shell.Tests.csproj -c Release --no-build
   dotnet test tests/MdReader.Integration.Tests/MdReader.Integration.Tests.csproj -c Release --no-build --filter "suite=blocking"
   dotnet test tests/MdReader.Integration.Tests/MdReader.Integration.Tests.csproj -c Release --no-build --filter "suite=nonblocking"
   ```

4. The previously observed baseline was 89 passing Core tests, 8 passing Shell
   tests, and 16 passing blocking integration tests. The non-blocking UI test
   failed at `UiSmokeTests.cs` when FlaUI attempted `Invoke()` on the Reader mode
   button because the UI Automation Invoke pattern was unavailable. Confirm the
   current behavior rather than assuming it remains unchanged.
5. Write a phased implementation plan. Implement the whole plan, not only the
   easiest items. Each phase should be independently testable.
6. Do not commit, push, publish packages, or create releases unless explicitly
   instructed.

## Phase 1: close verified implementation gaps

### 1. Fix the UI automation and accessibility failure

- Determine whether the failure is caused by an inaccessible WPF control, an
  incorrect FlaUI interaction, or both.
- Inspect the control with Windows UI Automation or Accessibility Insights when
  available. Ensure it exposes the correct role, accessible name, focus state,
  keyboard behavior, and supported activation pattern.
- Fix the production accessibility behavior if it is deficient. If the control
  is already correct, update the test to use the supported interaction without
  weakening its assertion.
- The non-blocking `Launch_open_toggle_close` test must pass locally.
- Add focused automation-property assertions where stable.

### 2. Add deterministic edit/save coverage to the blocking suite

- Protect the defining workflow end to end: open a Markdown file, enter source
  mode, change the buffer, save, return to reader mode, and verify both rendered
  content and exact bytes on disk.
- Do not depend on arbitrary sleeps or desktop focus. Prefer an explicit
  readiness protocol and a narrowly scoped test interface that invokes the same
  production buffer and save paths as the real UI.
- A test-only interface must be unavailable in production unless an explicit,
  unguessable test environment switch is set. It must not create a general IPC
  or document-script execution surface.
- Cover preservation of encoding, BOM, and line endings.
- Cover a save failure and verify that the original file remains intact and the
  user-facing state remains dirty.
- Move the new tests into the blocking CI lane.

### 3. Make reading statistics truthful and useful

- The current implementation counts raw Markdown characters, including fenced
  code, URLs, markup, and front matter, despite earlier documentation describing
  it as code-block-aware.
- Calculate reading statistics from meaningful readable content. Exclude YAML
  front matter, fenced/indented code, HTML tags, link destinations, image URLs,
  and Markdown syntax while retaining visible link text, image alt text, prose,
  headings, list text, quotations, and table cell text.
- Keep CJK behavior Unicode-aware. Handle supplementary-plane code points using
  `Rune` rather than assuming every character fits in one UTF-16 `char`.
- Avoid a second expensive parse when possible. Prefer deriving readable text or
  counts from the existing Markdig document tree during rendering.
- Document the exact definition and reading speeds.
- Add tests for prose, code-heavy documents, front matter, links, images, tables,
  emoji, CJK, mixed scripts, empty input, and pathological large input.

### 4. Complete performance measurement

- Add separate measurements for parse/render, heading/source-anchor processing,
  sanitization, image rewriting, HTML serialization/assembly, and total render.
- Add allocation and peak-memory reporting for representative, 1 MB, and 10 MB
  inputs.
- Replace the 110-second 10 MB budget with staged budgets that distinguish a
  catastrophic guardrail from an aspirational product target.
- Investigate the current large-document bottleneck. Improve it where profiling
  identifies safe, high-impact changes. Do not guess or trade away correctness.
- Keep long benchmarks out of ordinary unit tests. Ensure CI runtime remains
  reasonable by separating quick gating checks from scheduled full benchmarks.
- Make long rendering cancellable or supersedable so an edit or tab closure
  cannot leave obsolete work consuming resources.
- Update `docs/performance.md` with hardware, method, baseline, budget, and
  before/after results.

### 5. Finish documentation and screenshots

- Add screenshots of reader, source, and split modes captured from the current
  application at a consistent window size.
- Screenshots must show the surrounding application UI and demonstrate the
  command bar, tabs, TOC, reading statistics, active mode, and dirty indicator
  where appropriate. Use representative non-sensitive fixture content.
- Reconcile the changelog with the current full-width default. If the default
  changed after version 0.2, add an accurate later `Changed` entry instead of
  rewriting historical release notes misleadingly.
- Ensure README claims match shipped behavior and tests.

### 6. Improve recovery capacity behavior

- When the recovery store reaches its entry cap, evict the oldest safe snapshot
  rather than silently leaving a newly edited document unprotected.
- Never evict the snapshot for the document currently being updated.
- Surface a restrained, actionable warning when snapshots cannot be written or
  a document exceeds the recovery-size limit.
- Add tests for cap eviction, update-at-cap, oversized buffers, storage failure,
  stale cleanup, corrupt entries, and concurrent calls.

### 7. Complete CLI distribution investigation

- Produce clean-build measurements for the existing self-contained CLI, a
  framework-dependent build, a framework-dependent single-file build, and a
  Native AOT build where compatible.
- Install the required local build tools only if they are already approved for
  this environment; otherwise add a dedicated Windows CI experiment using the
  correct Visual Studio Build Tools workload.
- Audit trimming/AOT warnings and test AngleSharp, YamlDotNet, Markdig, every
  Markdown extension, HTML export, and error handling.
- Ship additional variants only when reliable. Keep the existing self-contained
  artifact as the safe default.
- Add packaging smoke tests, checksums, size tables, prerequisites, and clear
  artifact names. Do not claim Native AOT support if it remains experimental.

## Phase 2: improvements for normal users

### 8. Restore the previous session

- Add an opt-in setting, clearly presented on first relevant use, to restore the
  previous session.
- Persist only file paths and bounded UI state: tab order, active tab, mode,
  reader/editor position, TOC state, and split ratio. Never persist document
  contents outside the existing recovery mechanism.
- Restore existing files gracefully and report missing files without blocking
  startup.
- Add `Ctrl+Shift+T` to reopen the most recently closed tab, with a bounded
  history that is cleared appropriately.
- Ensure command-line file opening does not unexpectedly restore an unrelated
  session in automation/headless scenarios.
- Add unit and integration tests for restore, disabled restore, missing files,
  corrupt/version-mismatched state, tab order, active tab, and reopen-closed-tab.

### 9. Add pinned recent files

- Allow files in the recent list to be pinned and unpinned.
- Show pinned files first in the empty state and recent menu without creating a
  library, database, folder workspace, or vault concept.
- Retain pins when ordinary recent history is cleared, but provide an explicit
  way to clear both.
- Handle renamed, moved, unavailable, and removable-drive files gracefully.
- Persist a bounded, versioned model and migrate existing recent-file settings.

### 10. Improve missing local asset and link feedback

- Detect missing local images, linked documents, and unresolved in-document
  anchors.
- Render an unobtrusive inline warning that preserves the document layout and
  offers useful actions such as copy resolved path, open containing folder, or
  locate a moved image.
- Never probe remote links automatically. Do not create network traffic.
- Keep path resolution contained and secure against traversal or crafted URI
  input.
- Provide a document-level summary accessible from the command bar or status
  area, with source-line navigation.
- Add security and correctness tests for encoded paths, Unicode paths, spaces,
  fragments, traversal, UNC paths, symlinks/reparse points, and remote URLs.

### 11. Add safe image drag-and-drop in source mode

- When an image file is dropped into the source editor, offer to copy it into a
  configurable relative assets directory and insert a relative Markdown image
  reference at the cursor.
- Never overwrite an existing asset silently. Resolve filename collisions
  predictably and allow cancel.
- Support inserting a relative reference without copying when the image is
  already within the document tree.
- Preserve unsaved/dirty state and undo behavior as a single editor operation.
- Validate file type by content where practical, not extension alone.
- Do not upload or optimize images over the network.

### 12. Add export and print presets

- Provide a small set of understandable presets such as Document, Technical
  report, and Compact.
- Allow page size, margins, header/footer, code wrapping, content width, theme,
  and whether printed links include destinations.
- Keep the default workflow one click. Advanced options should not overwhelm
  ordinary users.
- Persist settings version-safely and make headless export accept the same
  profile configuration.
- Add deterministic export tests for each preset.

### 13. Add a first-run sample document

- Bundle a local sample demonstrating headings, TOC, links, code, math,
  Mermaid, tables, editing, shortcuts, and export.
- Show it only on a genuinely empty first launch. Do not recreate or reopen it
  after dismissal.
- Make it clear that it is a sample, not a user file, and prevent accidental
  overwriting of the bundled copy. “Save a copy” should be explicit.

### 14. Improve reading accessibility controls

- Add line spacing, paragraph spacing, and a small set of readable font presets,
  including a dyslexia-friendly option only if the font can be bundled under a
  compatible license or reliably selected from installed fonts.
- Support Windows high-contrast mode and reduced-motion preferences.
- Verify keyboard focus, screen-reader naming, scaling at 100–300%, and contrast
  in light, dark, system, and high-contrast themes.
- Avoid medical or accessibility efficacy claims that are not supported.

## Phase 3: improvements for developers

### 15. Add stdin/stdout CLI pipelines

- Support input from stdin and output to stdout without requiring temporary
  files.
- Design an unambiguous interface, for example:

  ```powershell
  Get-Content README.md -Raw | mdreader-convert --stdin --format html --stdout
  mdreader-convert README.md --format html --stdout
  ```

- Never mix diagnostics with stdout document content; diagnostics belong on
  stderr.
- Preserve deterministic exit codes and document them.
- Define base-directory behavior for resolving relative images and links from
  stdin, with an explicit `--base-dir` option.
- Add binary-safe redirection tests and UTF-8/UTF-16 input tests.

### 16. Add watch mode

- Add a debounced `--watch` mode for repeatedly rendering a file to HTML or PDF.
- Use the same robust file-change strategy as the GUI, including atomic-replace
  behavior and bounded retries.
- Print concise status to stderr and return meaningful exit behavior on
  cancellation and unrecoverable failure.
- Do not start a server or open a network listener.

### 17. Add machine-readable diagnostics and local link validation

- Add a diagnostic model for malformed front matter, invalid Mermaid/math,
  removed unsafe HTML, missing local images, broken local document links, and
  unresolved anchors.
- Support human-readable and JSON output with a documented, versioned schema.
- Include severity, code, message, document path, source line/column when known,
  and resolved target where safe.
- Add a local-only command for checking one document or a directory tree.
- Respect ignore patterns and avoid `bin`, `obj`, `.git`, and configurable
  directories.
- Do not validate remote URLs unless a future explicitly opt-in feature is
  designed and reviewed separately.
- Use distinct exit codes for success, validation findings, input errors, and
  internal failures.

### 18. Add reproducible rendering profiles

- Support a versioned `.mdreader.json` configuration for theme, width, raw-HTML
  policy, remote-image policy, export CSS, Mermaid behavior, and export presets.
- Define deterministic discovery and precedence: explicit CLI option, nearest
  config, user settings where appropriate, then defaults.
- Never allow repository configuration to weaken security silently. Raw HTML,
  remote content, external file access, and custom CSS require clearly defined
  safe behavior.
- Add schema documentation, examples, migration behavior, and config-validation
  diagnostics.

### 19. Add deterministic export mode

- Add an export option that removes environment-dependent metadata, normalizes
  ordering and line endings, and produces byte-stable HTML for identical input,
  configuration, and bundled dependency versions.
- Keep PDF limitations explicit; do not promise byte-identical PDF output unless
  it is actually achievable.
- Add golden tests that run the same export multiple times and compare bytes.

### 20. Add source-linked diagnostics in the GUI

- Display renderer/validation diagnostics in a compact document panel.
- Selecting an item must jump to the relevant source line in source or split
  mode.
- Do not show alarming warnings for extensions mdreader deliberately does not
  support; diagnostics should be actionable and suppressible by stable code.
- Reuse the CLI diagnostic model so GUI and CLI behavior cannot drift.

### 21. Add an official GitHub Action wrapper

- Create a small action that downloads or runs the existing CLI to render
  Markdown and perform local validation.
- Do not create a separate rendering implementation.
- Pin runtime/tool versions, support checksums, document permissions, and use
  least privilege.
- Include example workflows for HTML artifacts, PDF artifacts, and validation.
- Do not publish the action externally during this task; prepare and test the
  action in the repository unless publishing is explicitly authorized.

## Cross-cutting requirements

- Preserve sanitization, CSP, navigation interception, path containment, remote
  blocking, and WebView2 hardening. Treat all document and repository input as
  untrusted.
- Introduce no telemetry, background services, accounts, or unexpected network
  requests.
- Preserve byte-faithful saves and atomic-save behavior.
- Keep state versioned, bounded, corruption-tolerant, and under the existing
  application-data conventions.
- Keep startup and the basic open/read path simple. Advanced developer features
  belong in the CLI or unobtrusive secondary UI.
- Avoid new dependencies unless strongly justified. Pin versions, confirm
  compatible licensing, and update `THIRD-PARTY-NOTICES.md` where required.
- All long-running operations must support cancellation or supersession and must
  not update a disposed tab or stale document generation.
- All file operations must handle locks, permissions, deleted files, atomic
  replacement, Unicode, long paths, and removable/network volumes gracefully.
- Every user-facing error must be actionable and must not expose stack traces.
- Meet keyboard, screen-reader, high-contrast, focus, scaling, and WCAG AA
  expectations.

## Required verification

At minimum, run and report:

```powershell
dotnet format MdReader.slnx --verify-no-changes --no-restore
dotnet build MdReader.slnx -c Release --no-restore
dotnet test tests/MdReader.Core.Tests/MdReader.Core.Tests.csproj -c Release --no-build
dotnet test tests/MdReader.Shell.Tests/MdReader.Shell.Tests.csproj -c Release --no-build
dotnet test tests/MdReader.Integration.Tests/MdReader.Integration.Tests.csproj -c Release --no-build --filter "suite=blocking"
dotnet test tests/MdReader.Integration.Tests/MdReader.Integration.Tests.csproj -c Release --no-build --filter "suite=nonblocking"
dotnet run --project benchmarks/MdReader.Benchmarks -c Release -- --check
```

Also run focused tests for CLI redirection, deterministic output, configuration,
link validation, session state, recovery, packaging variants, and export
presets. Exercise representative UI flows manually where automation cannot
prove layout or accessibility.

Do not run build/test commands concurrently when they share `bin` or `obj`
directories; previous parallel verification caused transient compiler file-lock
errors on Windows. Serialize those commands or use isolated output paths.

## Completion report

Provide:

1. A concise summary grouped by gap fixes, normal-user improvements, developer
   improvements, accessibility/security, tests, performance, packaging, and
   documentation.
2. Exact commands and results, including test counts.
3. Before/after performance, memory, startup, and package-size measurements.
4. Screenshots of the final reader, source, and split interfaces.
5. A list of modified and newly created files.
6. Any incomplete requirement, with the concrete technical reason, evidence of
   attempted alternatives, and the smallest follow-up. Do not silently omit,
   weaken, or describe an unimplemented requirement as complete.
7. Remaining risks and manual checks, especially Windows accessibility,
   WebView2 behavior, installer/MSIX behavior, recovery under process failure,
   and CLI behavior under redirection.
