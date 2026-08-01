# Contributing to mdreader

Thanks for considering it. mdreader is deliberately small — a reader that
happens to edit — so the bar for new surface area is high, and the bar for
fixes, polish, and performance is low. When in doubt, open an issue first.

## Ground rules

- **Scope**: check the "What this is not" section of the README before
  proposing features. Vaults, sync, plugins, WYSIWYG, and AI are out.
- **Every design decision resolves in favor of fast, beautiful reading.**
- **Security posture is non-negotiable**: documents are untrusted input. Any
  change touching rendering, sanitization, CSP, navigation, or file
  association must keep the tests in `XssSecurityTests` and
  `FileAssociationRegistrarTests` green and will get extra review.
- **No telemetry**, no network calls beyond the two documented opt-ins.

## Building

```powershell
dotnet build            # requires the .NET 10 SDK
dotnet test             # full suite; integration tests launch the real app
dotnet format           # run before committing — CI enforces it
```

The WPF app lives in `src/MdReader.App`, the rendering pipeline in
`src/MdReader.Core` (start there — it has no UI dependencies), shell
integration in `src/MdReader.Shell`, and the bundled web assets in
`src/MdReader.Web`.

## Tests

- Rendering changes: update or add a fixture in `fixtures/` and re-verify the
  golden snapshot (`Snapshots/*.verified.html`). Review the diff — that *is*
  the change you're making.
- Anything touching encodings or line endings needs a round-trip test.
- Registry code is tested against a sandbox subkey — never write tests that
  touch the real association state of the machine.

## Pull requests

- Small, focused, with a clear description of the user-visible effect.
- CI must pass: build, `dotnet format --verify-no-changes`, unit tests.
- Follow [Keep a Changelog](https://keepachangelog.com) — add your change to
  the Unreleased section of `CHANGELOG.md`.
- Versioning is [SemVer](https://semver.org).
