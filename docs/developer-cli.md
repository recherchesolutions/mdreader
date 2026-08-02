# Developer CLI

`mdreader-convert` uses the same Markdig pipeline, sanitizer, image policy, and
HTML assembly code as the desktop application.

Releases provide a self-contained single executable and a smaller
framework-dependent ZIP for machines with the .NET 10 runtime. A manual CI
workflow builds and smoke-tests Native AOT experimentally; it is not promoted
to a release artifact unless the compatibility audit succeeds.

The clean framework-dependent baseline is 2.63 MB across 12 files before ZIP
compression, compared with roughly 72 MB for the self-contained single file.

## Conversion

```powershell
mdreader-convert input.md output.html
mdreader-convert input.md --stdout
Get-Content input.md -Raw | mdreader-convert --stdin --stdout --base-dir .
```

Document output goes to stdout only with `--stdout`. Status and diagnostics
always go to stderr, so redirection is safe. `--deterministic` normalizes HTML
line endings for source-control and golden-file workflows.

## Watch mode

```powershell
mdreader-convert input.md output.html --watch
```

Watch mode listens only to the local input file, debounces replacement/write
bursts, and never starts a web server or network listener. Press `Ctrl+C` to
stop it.

## Local validation

```powershell
mdreader-convert --check-links input.md
mdreader-convert --check-links docs --diagnostics json
```

Validation checks local image paths, linked files, and document anchors. It
never contacts remote URLs. JSON output is JSON Lines on stderr with schema
version 1, diagnostic code, severity, message, path, source line, and target.

## Configuration

The converter searches from the input directory toward the filesystem root for
`.mdreader.json`:

```json
{
  "version": 1,
  "theme": "dark",
  "deterministic": true,
  "contentWidth": 960,
  "customCss": "docs/export-theme.css"
}
```

Explicit command-line options take precedence. Repository configuration cannot
enable raw HTML or remote content. See
[`mdreader-config.schema.json`](mdreader-config.schema.json) for the schema.

## Exit codes

| Code | Meaning |
|---:|---|
| 0 | Success |
| 1 | Conversion, I/O, or internal failure |
| 2 | Invalid command or input |
| 4 | Local validation findings |

## GitHub Actions

The repository-local composite action builds the converter, validates local
targets, and optionally produces deterministic HTML:

```yaml
- uses: ./.github/actions/mdreader
  with:
    path: docs
```

It wraps the existing CLI rather than maintaining a second renderer.
