# Microsoft Store packaging (MSIX)

The Store build is the same app packaged as MSIX. In packaged mode mdreader
automatically:

- skips registry file-association writes (the manifest declares them — see
  `Package.appxmanifest.template`),
- hides the GitHub update-check setting (the Store handles updates),
- hides the extension opt-in checkboxes (associations are fixed by the manifest).

Everything else — reader, editor, export, settings — is identical to the
installer/portable builds. Detection lives in
[`PackagedContext.cs`](../../src/MdReader.App/Services/PackagedContext.cs).

## One-time Partner Center setup

1. **Apps and games → New product → App**, reserve the name **mdreader**.
2. Open **Product management → Product identity** and note:
   - `Package/Identity/Name` (e.g. `12345RechercheSolutions.mdreader`)
   - `Package/Identity/Publisher` (e.g. `CN=xxxxxxxx-xxxx-…`)
   - `Publisher display name`

## Building the package

Requires the Windows SDK (`winget install Microsoft.WindowsSDK.10.0.26100`).

```powershell
.\packaging\msix\build-msix.ps1 -Version <version> `
  -IdentityName "<Package/Identity/Name>" `
  -Publisher "<Package/Identity/Publisher>" `
  -PublisherDisplayName "<Publisher display name>"
```

Produces `artifacts\mdreader-<version>.msix`. **No signing needed for Store
uploads** — Microsoft signs during ingestion.

## Submitting

1. In Partner Center open the app → **Start your submission**.
2. **Packages**: upload the `.msix`.
3. **Properties/listing**: category *Productivity*; the listing text can be
   lifted from the repo README; screenshots from `docs/`.
4. **Privacy policy URL**: required even though mdreader collects nothing — a
   one-line page on recherchesolutions.com ("mdreader collects no data") works.
5. Age ratings questionnaire → certify → submit. First certification usually
   takes 1–3 business days.

## Sideload testing (optional)

Store uploads don't need signing, but to install the .msix locally you must
sign it with a cert that matches the Publisher CN and is trusted on the
machine (`New-SelfSignedCertificate` + `signtool` + install cert to Trusted
People). For most changes, testing the unpackaged build is equivalent — the
packaged-mode differences are limited to what `PackagedContext` gates.

## Store vs winget vs GitHub

All three channels ship the same code and coexist:

| Channel | Artifact | Updates |
|---|---|---|
| GitHub Releases | Inno setup.exe + portable ZIP | manual / opt-in check |
| winget | same setup.exe | `winget upgrade` |
| Microsoft Store | this MSIX | automatic |
