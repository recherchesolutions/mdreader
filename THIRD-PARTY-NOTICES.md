# Third-party notices

mdreader bundles or depends on the following components. Each license permits
redistribution; attribution requirements are satisfied by this file and the
license texts shipped in `src/MdReader.Web/vendor/*/LICENSE.txt`.

## Bundled web assets (shipped inside the app)

| Component | Version | License | Notes |
|---|---|---|---|
| [Monaco Editor](https://github.com/microsoft/monaco-editor) | 0.52.2 | MIT | Source-mode editor. Pruned to the markdown-relevant subset (no TS/CSS/HTML/JSON language services, English UI only). |
| [highlight.js](https://github.com/highlightjs/highlight.js) | 11.11.1 | BSD-3-Clause | Common-languages browser bundle. |
| [Mermaid](https://github.com/mermaid-js/mermaid) | 11.16.0 | MIT | Runs with `securityLevel: 'strict'`. |
| [KaTeX](https://github.com/KaTeX/KaTeX) | 0.18.1 | MIT | Including its bundled fonts (SIL OFL 1.1 for the font files). |

## NuGet dependencies

| Package | License | Used for |
|---|---|---|
| [Markdig](https://github.com/xoofx/markdig) | BSD-2-Clause | Markdown parsing and rendering |
| [HtmlSanitizer](https://github.com/mganss/HtmlSanitizer) | MIT | HTML allowlist sanitization |
| [AngleSharp](https://github.com/AngleSharp/AngleSharp) | MIT | DOM processing (via HtmlSanitizer and the image policy pass) |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | MIT | YAML front matter parsing |
| [Microsoft.Web.WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) | Microsoft license (redistributable SDK) | Rendering host |

## Test/build-only dependencies (not distributed)

xUnit (Apache-2.0), FluentAssertions 7.x (Apache-2.0 — deliberately pinned
below v8, which changed to a paid commercial license), Verify (MIT), FlaUI
(MIT), coverlet (MIT), BenchmarkDotNet (MIT), Inno Setup (Inno Setup License).

## Fonts

The reader uses the system UI font stack (Segoe UI Variable, Cascadia Code,
Consolas) — no font files are redistributed except KaTeX's math fonts noted
above.
