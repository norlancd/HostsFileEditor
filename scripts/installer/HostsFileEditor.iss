; Inno Setup script for Hosts File Editor.
; This is a generic template driven entirely by /D defines passed on the ISCC command line by
; scripts\New-InnoInstaller.ps1 - do not compile it directly. Required defines:
;   MyFlavor      classic | modern
;   MyArch        x64 (used only for the output file name)
;   MyVersion     e.g. 1.3.0.0
;   MySourceDir   folder whose contents get installed (the dotnet publish output)
;   MyAppExeName  main executable file name, e.g. HostsFileEditor.exe
;   MyAppIcon     path to a .ico used for the installer and shortcuts
;   MyOutputDir   where the generated setup.exe is written

#define MyAppName "Hosts File Editor"
#define MyAppPublisher "Scott Lerch"
#define MyAppURL "https://github.com/scottlerch/HostsFileEditor"

; Stable, per-flavor AppId so classic and modern install/upgrade independently and don't clobber each
; other's uninstall entry. These GUIDs are arbitrary but must never change for a given flavor.
#if MyFlavor == "classic"
  #define MyAppId "7B9E4C2A-1D3F-4A6B-8C5E-0F1A2B3C4D5E"
  #define MyAppNameFull "Hosts File Editor"
#else
  #define MyAppId "8C0F5D3B-2E4A-5B7C-9D6F-1A2B3C4D5E6F"
  #define MyAppNameFull "Hosts File Editor (Modern)"
#endif

[Setup]
AppId={{{#MyAppId}}
AppName={#MyAppNameFull}
AppVersion={#MyVersion}
AppVerName={#MyAppNameFull} {#MyVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppNameFull}
DefaultGroupName={#MyAppNameFull}
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppNameFull} {#MyVersion}
OutputDir={#MyOutputDir}
OutputBaseFilename=HostsFileEditor-{#MyFlavor}-{#MyArch}-setup
Compression=lzma2
SolidCompression=yes
; Hosts File Editor writes to C:\Windows\System32\drivers\etc\hosts, so it runs elevated; install
; machine-wide into Program Files, which also requires elevation.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile={#MyAppIcon}
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Install the published payload, skipping build/packaging leftovers that may be sitting in the publish
; dir (debug symbols, the MSIX, the portable zip, the renamed portable exe, and the MSIX manifest).
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion; Excludes: "*.pdb,*.msix,*.zip,{#MyAppExeName}.config.bak,HostsFileEditor-portable.exe,AppxManifest.xml"

[Icons]
Name: "{group}\{#MyAppNameFull}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppNameFull}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; runascurrentuser: the installer is already elevated and the app requires admin, so launch it in the
; same elevated context instead of triggering a second UAC prompt.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppNameFull, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runascurrentuser
