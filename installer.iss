; Inno Setup script for Claude Code Tray
; Build:
;   1) dotnet publish -c Release
;   2) "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss
; Output: dist\ClaudeTray-Setup.exe

#define MyAppName "Claude Code Tray"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Alexandre Oliveira"
#define MyAppExeName "ClaudeTray.exe"
#define MyPublishDir "bin\Release\net10.0-windows\win-x64\publish"

[Setup]
AppId={{8F3C2A14-7B6D-4E9A-9C21-CLAUDETRAY001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
; Per-user install: no admin rights required.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\ClaudeTray
DisableProgramGroupPage=yes
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=dist
OutputBaseFilename=ClaudeTray-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startupicon"; Description: "Iniciar o {#MyAppName} com o Windows"; GroupDescription: "Inicialização:"
Name: "startmenuicon"; Description: "Criar atalho no Menu Iniciar"; GroupDescription: "Atalhos:"

[Files]
Source: "{#MyPublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"; Tasks: startmenuicon

[Registry]
; Autostart per-user (mirrors the in-app "Start with Windows" toggle).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "ClaudeTray"; ValueData: """{app}\{#MyAppExeName}"""; \
    Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Iniciar o {#MyAppName} agora"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
; Best-effort: close the running tray app before uninstalling.
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "KillTray"
