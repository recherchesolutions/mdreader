; mdreader — Inno Setup installer
; ============================================================================
; Installer choice: Inno Setup (not WiX). WiX v7 requires a EULA acceptance and
; an Open Source Maintenance Fee for organizations above US$10k annual revenue;
; the project owner chose Inno Setup as the approved fee-free alternative.
;
; Per-user install by default (no elevation) into %LOCALAPPDATA%\Programs.
; File association model (spec §4): the app itself maintains its HKCU
; registration (ProgId + Capabilities + additive OpenWithProgids) on first run,
; and the installer writes the same keys so "Open with" works before first
; launch. Neither the installer nor the app EVER touches UserChoice.
;
; Silent install: mdreader-setup.exe /VERYSILENT /NORESTART
;   optional:     /MERGETASKS="desktopicon,ext_mdown,..." /D=<dir via /DIR=>
; ============================================================================

#ifndef AppVersion
  #define AppVersion "0.4.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\publish\app"
#endif

[Setup]
AppId={{7E1B7A70-6F44-4E7B-9E63-2C6C1F1B8D11}
AppName=mdreader
AppVersion={#AppVersion}
AppPublisher=mdreader contributors
AppPublisherURL=https://github.com/recherchesolutions/mdreader
AppSupportURL=https://github.com/recherchesolutions/mdreader/issues
AppUpdatesURL=https://github.com/recherchesolutions/mdreader/releases
DefaultDirName={autopf}\mdreader
DefaultGroupName=mdreader
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputBaseFilename=mdreader-setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Per-user by default, no elevation. "PrivilegesRequiredOverridesAllowed"
; surfaces a per-machine option (which does require elevation) in the wizard
; and via /ALLUSERS.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
UninstallDisplayIcon={app}\mdreader.exe
ChangesAssociations=yes
MinVersion=10.0.17763
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "startmenuicon"; Description: "Create a Start Menu shortcut"; GroupDescription: "Shortcuts:"
Name: "desktopicon"; Description: "Create a Desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "openwithmenu"; Description: "Add ""Open with mdreader"" to the Explorer context menu"; GroupDescription: "Shell integration:"
; Opt-in extra extensions (spec §4.1). .md and .markdown are always declared.
Name: "ext_mdown";  Description: "Register .mdown";  GroupDescription: "Additional file types:"; Flags: unchecked
Name: "ext_mkd";    Description: "Register .mkd";    GroupDescription: "Additional file types:"; Flags: unchecked
Name: "ext_mkdn";   Description: "Register .mkdn";   GroupDescription: "Additional file types:"; Flags: unchecked
Name: "ext_mdtxt";  Description: "Register .mdtxt";  GroupDescription: "Additional file types:"; Flags: unchecked
Name: "ext_mdtext"; Description: "Register .mdtext"; GroupDescription: "Additional file types:"; Flags: unchecked
Name: "ext_mdx";    Description: "Register .mdx (renders as plain markdown)"; GroupDescription: "Additional file types:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\mdreader"; Filename: "{app}\mdreader.exe"; Tasks: startmenuicon
Name: "{autodesktop}\mdreader"; Filename: "{app}\mdreader.exe"; Tasks: desktopicon

[Registry]
; ---- ProgId (HKA = HKCU for per-user installs, HKLM for per-machine) --------
Root: HKA; Subkey: "Software\Classes\MdReader.Markdown.1"; ValueType: string; ValueData: "Markdown Document"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\MdReader.Markdown.1"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "Markdown Document"
Root: HKA; Subkey: "Software\Classes\MdReader.Markdown.1\DefaultIcon"; ValueType: string; ValueData: """{app}\mdreader-doc.ico"""
Root: HKA; Subkey: "Software\Classes\MdReader.Markdown.1\shell\open\command"; ValueType: string; ValueData: """{app}\mdreader.exe"" ""%1"""
Root: HKA; Subkey: "Software\Classes\MdReader.Markdown.1\shell\edit\command"; ValueType: string; ValueData: """{app}\mdreader.exe"" --source ""%1"""

; ---- Applications entry ------------------------------------------------------
Root: HKA; Subkey: "Software\Classes\Applications\mdreader.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "mdreader"; Flags: uninsdeletekey
; Windows records an Open With choice as Applications\mdreader.exe, so this key
; needs its own DefaultIcon or associated files show a blank icon.
Root: HKA; Subkey: "Software\Classes\Applications\mdreader.exe\DefaultIcon"; ValueType: string; ValueData: """{app}\mdreader-doc.ico"""
Root: HKA; Subkey: "Software\Classes\Applications\mdreader.exe\shell\open\command"; ValueType: string; ValueData: """{app}\mdreader.exe"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\mdreader.exe\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\mdreader.exe\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""

; ---- Capabilities + RegisteredApplications ----------------------------------
Root: HKA; Subkey: "Software\mdreader\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "mdreader"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\mdreader\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Fast markdown reader and editor"
Root: HKA; Subkey: "Software\mdreader\Capabilities\FileAssociations"; ValueType: string; ValueName: ".md"; ValueData: "MdReader.Markdown.1"
Root: HKA; Subkey: "Software\mdreader\Capabilities\FileAssociations"; ValueType: string; ValueName: ".markdown"; ValueData: "MdReader.Markdown.1"
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "mdreader"; ValueData: "Software\mdreader\Capabilities"; Flags: uninsdeletevalue

; ---- ADDITIVE OpenWithProgids. NEVER the (Default) value, NEVER UserChoice. --
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "MdReader.Markdown.1"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "MdReader.Markdown.1"; ValueData: ""; Flags: uninsdeletevalue

; ---- Opt-in extensions -------------------------------------------------------
Root: HKA; Subkey: "Software\Classes\.mdown\OpenWithProgids"; ValueType: string; ValueName: "MdReader.Markdown.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: ext_mdown
Root: HKA; Subkey: "Software\mdreader\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mdown"; ValueData: "MdReader.Markdown.1"; Tasks: ext_mdown
Root: HKA; Subkey: "Software\Classes\.mkd\OpenWithProgids"; ValueType: string; ValueName: "MdReader.Markdown.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: ext_mkd
Root: HKA; Subkey: "Software\mdreader\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mkd"; ValueData: "MdReader.Markdown.1"; Tasks: ext_mkd
Root: HKA; Subkey: "Software\Classes\.mkdn\OpenWithProgids"; ValueType: string; ValueName: "MdReader.Markdown.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: ext_mkdn
Root: HKA; Subkey: "Software\mdreader\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mkdn"; ValueData: "MdReader.Markdown.1"; Tasks: ext_mkdn
Root: HKA; Subkey: "Software\Classes\.mdtxt\OpenWithProgids"; ValueType: string; ValueName: "MdReader.Markdown.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: ext_mdtxt
Root: HKA; Subkey: "Software\mdreader\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mdtxt"; ValueData: "MdReader.Markdown.1"; Tasks: ext_mdtxt
Root: HKA; Subkey: "Software\Classes\.mdtext\OpenWithProgids"; ValueType: string; ValueName: "MdReader.Markdown.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: ext_mdtext
Root: HKA; Subkey: "Software\mdreader\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mdtext"; ValueData: "MdReader.Markdown.1"; Tasks: ext_mdtext
Root: HKA; Subkey: "Software\Classes\.mdx\OpenWithProgids"; ValueType: string; ValueName: "MdReader.Markdown.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: ext_mdx
Root: HKA; Subkey: "Software\mdreader\Capabilities\FileAssociations"; ValueType: string; ValueName: ".mdx"; ValueData: "MdReader.Markdown.1"; Tasks: ext_mdx

; ---- Optional Explorer context menu (per-task) -------------------------------
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.md\shell\mdreader"; ValueType: string; ValueData: "Open with mdreader"; Flags: uninsdeletekey; Tasks: openwithmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.md\shell\mdreader"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\mdreader.exe"",0"; Tasks: openwithmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.md\shell\mdreader\command"; ValueType: string; ValueData: """{app}\mdreader.exe"" ""%1"""; Tasks: openwithmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.markdown\shell\mdreader"; ValueType: string; ValueData: "Open with mdreader"; Flags: uninsdeletekey; Tasks: openwithmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.markdown\shell\mdreader\command"; ValueType: string; ValueData: """{app}\mdreader.exe"" ""%1"""; Tasks: openwithmenu

[Run]
Filename: "{app}\mdreader.exe"; Description: "Launch mdreader"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Only the WebView2 cache — settings are removed only when the user opts in
; (see [Code] below).
Type: filesandordirs; Name: "{userappdata}\mdreader\webview2"

[Code]
const
  WebView2RegKey = 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  BootstrapperUrl = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';

var
  NeedsWebView2: Boolean;

function IsWebView2RuntimeInstalled(): Boolean;
var
  Version: string;
begin
  // Evergreen runtime writes pv under EdgeUpdate\Clients (HKLM 32/64) or HKCU.
  Result :=
    (RegQueryStringValue(HKLM64, WebView2RegKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKLM32, WebView2RegKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU,   WebView2RegKey, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

procedure InitializeWizard();
begin
  NeedsWebView2 := not IsWebView2RuntimeInstalled();
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  BootstrapperPath: string;
  ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and NeedsWebView2 then
  begin
    // Evergreen Bootstrapper: small stub that installs the Evergreen runtime.
    // Per spec §8.1: do not bundle the fixed-version runtime.
    BootstrapperPath := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
    if DownloadTemporaryFile(BootstrapperUrl, 'MicrosoftEdgeWebview2Setup.exe', '', nil) > 0 then
    begin
      Exec(BootstrapperPath, '/silent /install', '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
    end
    else
    begin
      SuppressibleMsgBox(
        'mdreader needs the Microsoft WebView2 Runtime, which could not be downloaded. ' +
        'Install it from https://developer.microsoft.com/microsoft-edge/webview2/ and run mdreader again.',
        mbInformation, MB_OK, IDOK);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Clean uninstall of settings only when the user opts in (spec §8.1).
    // Silent uninstalls never delete settings.
    if not UninstallSilent() then
    begin
      if SuppressibleMsgBox('Also remove your mdreader settings and custom themes?',
          mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
      begin
        DelTree(ExpandConstant('{userappdata}\mdreader'), True, True, True);
      end;
    end;
  end;
end;
