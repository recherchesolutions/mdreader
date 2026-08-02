<#
.SYNOPSIS
Builds the Microsoft Store MSIX package for mdreader.

.DESCRIPTION
Publishes the app (unless -PublishDir points at an existing publish), stages it
with the manifest and Store assets, and packs an .msix with MakeAppx. The Store
signs packages during ingestion, so no certificate is needed for Store uploads.
(To sideload-test locally you would need to sign with your own cert; see the
README in this folder.)

.EXAMPLE
.\build-msix.ps1 -Version 0.4.0 `
  -IdentityName "12345RechercheSolutions.mdreader" `
  -Publisher "CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX" `
  -PublisherDisplayName "Recherche Solutions LLC"
#>
param(
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [string]$IdentityName,
    [Parameter(Mandatory)] [string]$Publisher,
    [Parameter(Mandatory)] [string]$PublisherDisplayName,
    [string]$PublishDir,
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path "$PSScriptRoot\..\.."
$msixRoot = "$PSScriptRoot"
if (-not $OutputPath) { $OutputPath = "$repoRoot\artifacts\mdreader-$Version.msix" }

# 1. Locate MakeAppx (Windows SDK).
$makeAppx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeAppx) {
    throw "MakeAppx.exe not found. Install the Windows SDK (winget install Microsoft.WindowsSDK.10.0.26100) and retry."
}

# 2. Publish the app if no publish dir was supplied.
if (-not $PublishDir) {
    $PublishDir = "$repoRoot\publish\app"
    Write-Host "Publishing app ($Version)..."
    dotnet publish "$repoRoot\src\MdReader.App" -c Release -r win-x64 --self-contained true `
        -p:PublishReadyToRun=true -p:Version=$Version -o $PublishDir -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
}

# 3. Stage: app\ payload + Assets + manifest with identity substituted.
$staging = Join-Path ([IO.Path]::GetTempPath()) "mdreader-msix-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force "$staging\app", "$staging\Assets" | Out-Null
Copy-Item "$PublishDir\*" "$staging\app" -Recurse
Copy-Item "$msixRoot\Assets\*" "$staging\Assets"

$manifest = Get-Content "$msixRoot\Package.appxmanifest.template" -Raw
$manifest = $manifest.Replace("{{IDENTITY_NAME}}", $IdentityName)
$manifest = $manifest.Replace("{{PUBLISHER}}", $Publisher)
$manifest = $manifest.Replace("{{PUBLISHER_DISPLAY_NAME}}", $PublisherDisplayName)
$manifest = $manifest.Replace("{{VERSION}}", $Version)
[IO.File]::WriteAllText("$staging\AppxManifest.xml", $manifest)

# 4. Pack.
New-Item -ItemType Directory -Force (Split-Path $OutputPath) | Out-Null
& $makeAppx.FullName pack /d $staging /p $OutputPath /o
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed" }

Remove-Item $staging -Recurse -Force
Write-Host ""
Write-Host "MSIX written to $OutputPath"
Write-Host "Upload it in Partner Center: your app -> Packages -> drag the .msix in."
