# winget packaging

`winget install mdreader` is the single highest-leverage distribution channel.

## First-time submission (manual, once)

1. Publish a GitHub release with `mdreader-setup-<version>.exe` attached.
2. Install the manifest tool: `winget install wingetcreate`.
3. Generate and submit:

   ```powershell
   wingetcreate new https://github.com/recherchesolutions/mdreader/releases/download/v<version>/mdreader-setup-<version>.exe
   ```

   Use `RechercheSolutions.mdreader` as the package identifier and fill in the
   metadata from `manifest-template.yaml` in this folder.
4. `wingetcreate submit` opens the PR against `microsoft/winget-pkgs`.

## Every release after

`.github/workflows/winget.yml` submits the update automatically when a release
is published. It needs a `WINGET_TOKEN` repository secret (classic PAT,
`public_repo` scope).
