# Changelog

All notable changes to mdreader are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - 2026-08-01

### Added

- About dialog with version, license note, and a link to Recherche Solutions LLC.

### Fixed

- Settings window: content no longer clips at the bottom (scrollable and
  resizable now).

## [0.2.0] - 2026-08-01

### Added

- Content width setting: Default (720 px reading measure), Wide, Extra wide,
  or Full width — applies to the reader and to HTML/clipboard export.

### Fixed

- The "Make mdreader the default app" bar no longer reappears after the user
  has already set mdreader as the default (Windows records the Open With
  choice as `Applications\mdreader.exe`, which was not recognized).

## [0.1.0] - 2026-08-01

### Added

- Reader mode: CommonMark + GFM rendering (Markdig) with tables, task lists,
  footnotes, definition lists, abbreviations, citations, custom containers,
  emoji, YAML front matter as a collapsed metadata card.
- highlight.js code coloring with hover copy button; Mermaid diagrams with
  inline error fallback; KaTeX math. All assets bundled — fully offline.
- Source mode: Monaco editor, `Ctrl+E` toggle with scroll-position mapping,
  split view with synchronized scrolling.
- Byte-faithful saves: encoding, BOM, and line endings are preserved.
- Live reload with 250 ms debounce and a Reload/Keep-mine bar for dirty
  buffers.
- Single instance with tabs, drag & drop, recent files, find in both modes,
  persistent zoom and window placement, light/dark themes following Windows,
  custom CSS themes from `%APPDATA%\mdreader\themes`.
- Export to self-contained HTML and PDF, print, copy as rich text (CF_HTML);
  headless `--export-html` / `--export-pdf`; `mdreader-convert` CLI.
- Per-user file association registration (additive, never UserChoice) with a
  polite first-run prompt using the standard Windows dialog.
- Security: allowlist HTML sanitization, strict CSP, remote images blocked by
  default, navigation interception, path containment, WebView2 hardening.
- Inno Setup installer (per-user, silent-install capable), portable ZIP,
  winget workflow.

[0.3.0]: https://github.com/mdreader/mdreader/commits/main
[0.2.0]: https://github.com/mdreader/mdreader/commits/main
[0.1.0]: https://github.com/mdreader/mdreader/commits/main
