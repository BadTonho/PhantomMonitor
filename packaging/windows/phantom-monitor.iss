; Phantom Monitor - instalador Windows por usuário (Inno Setup 6).
;
; Geração local/manual (o projeto não tem CI):
;   scripts/release.ps1 -Version <x.y.z> -PackWindowsSetup  (descobre o ISCC)
; ou direto:
;   ISCC.exe /DVersionName=1.2.3 /DReleaseDir=releases\stage\win /O<saída> packaging\windows\phantom-monitor.iss
;
; Instala em %LOCALAPPDATA%\Programs\PhantomMonitor (per-user) — a MESMA pasta
; dos updates automáticos: o apply_update.cmd gerado pelo updater troca só os
; arquivos dessa pasta com backup/rollback, então o conjunto de arquivos
; instalado precisa ser o mesmo do portable zip (exe + PresentMon.exe +
; config.example.json). O config.json real do usuário fica em %APPDATA% e
; todos os logs em %LOCALAPPDATA%\PhantomMonitor\logs, fora da troca.

#ifndef VersionName
  #define VersionName "0.0.0-local"
#endif

; Pasta do publish do Windows (stage do scripts/release.ps1) usada como fonte.
#ifndef ReleaseDir
  #define ReleaseDir "..\..\releases\stage\win"
#endif

[Setup]
AppId={{8D5E4F61-A1B4-4F0E-8E7A-3D9C2F0B7A11}}
AppName=Phantom Monitor
AppVersion={#VersionName}
AppPublisher=BadTonho
; Per-user: sem elevação, pasta sob o perfil do usuário (igual a LocalAppData do updater).
DefaultDirName={localappdata}\Programs\PhantomMonitor
DirExistsWarning=no
DisableDirPage=yes
DefaultGroupName=Phantom Monitor
DisableProgramGroupPage=yes
OutputBaseFilename=PhantomMonitorSetup-{#VersionName}
PrivilegesRequired=lowest
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\PhantomMonitor.exe

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#ReleaseDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs replacesameversion ignoreversion

[Icons]
Name: "{autoprograms}\Phantom Monitor"; Filename: "{app}\PhantomMonitor.exe"
Name: "{autodesktop}\Phantom Monitor"; Filename: "{app}\PhantomMonitor.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PhantomMonitor.exe"; Description: "{cm:LaunchProgram,Phantom Monitor}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; logs/sensors sobem de novo em execução; nada de config do usuário (%APPDATA%).
Type: filesandordirs; Name: "{app}\logs"
