# Product recommendations

mdreader already has a strong, differentiated position: it is a fast Windows
document reader that happens to edit, rather than another knowledge-management
system. Future work should protect that simplicity.

## Priorities

### 1. Treat opening speed as a product feature

Add cold-start and large-file benchmarks to CI, with explicit performance
budgets such as time to first readable content and time to open a 10 MB file.
Performance is one of mdreader's clearest differentiators and should be guarded
against regressions.

### 2. Improve document navigation

Prioritize navigation before adding more Markdown extensions:

- A persistent, collapsible table-of-contents sidebar.
- `Ctrl+G` navigation to a heading or source line.
- Back and forward navigation after following internal links.
- Restoration of each tab's scroll position across restarts.

### 3. Add unobtrusive reading information

The status bar could show the current line or section, total word count, reading
progress, and estimated reading time. Keep this compact so the reader remains
calm and uncluttered.

### 4. Make editing and recovery safer

- Recover unsaved buffers after a crash without silently overwriting files.
- Display a clear dirty marker on modified tabs.
- When closing the window, list every document with unsaved changes.
- Use atomic saves through a temporary file and replacement operation.

### 5. Strengthen deterministic UI testing

The core and shell have useful automated coverage, while UI integration tests
are allowed to fail in CI because hosted WebView2 automation can be flaky.
Extract a small blocking smoke suite covering the critical path: open, render,
edit, save, reload, and export. Broader UI tests can remain non-blocking.

### 6. Reduce distribution friction

The current portable package is roughly 66 MB and the standalone converter is
roughly 72 MB. Investigate framework-dependent and Native AOT CLI builds,
trimming where safe, and publish size and startup comparisons. The GUI's size
is more understandable because it bundles WPF support, Monaco, Mermaid, KaTeX,
and other offline assets.

### 7. Refine the application chrome

The document typography is clean, but the surrounding WPF interface is fairly
conventional. A restrained command bar, stronger active-tab treatment, and an
integrated table-of-contents control would make the product feel more cohesive
without compromising its native Windows character.

### 8. Tighten the public documentation

- Reconcile the README statement that content follows the window width by
  default with the changelog statement that the default is a 720 px measure.
- Document the full winget identifier, `recherchesolutions.mdreader`, if the
  shorter `winget install mdreader` command is not guaranteed to resolve.
- Add screenshots of source and split modes.
- Add a short comparison with browser previews, VS Code preview, and note apps
  to clarify why someone should choose mdreader.

## Scope to protect

Avoid adding AI features, cloud sync, vaults, backlinks, WYSIWYG editing, or a
general plugin ecosystem. These would weaken the product's reader-first focus.
Custom CSS themes provide an appropriately small extensibility surface.

## Suggested next release

If only one user-facing feature is selected, build a polished persistent
table-of-contents and navigation experience. If only one engineering investment
is selected, add automated startup and large-document performance budgets.
