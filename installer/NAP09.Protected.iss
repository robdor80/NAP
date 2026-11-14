; NAP09 — protected distribution ONLY. Never installs a writer, service,
; launch configuration, secrets, data roots or a production permission grant.
; Separate AppId from NAP06 per-user; no migration, no automatic cutover.
#ifndef AppVersion
  #error "Required: /DAppVersion"
#endif
#ifndef PayloadDir
  #error "Required: /DPayloadDir"
#endif
#ifndef ReleaseOutput
  #error "Required: /DReleaseOutput"
#endif
#ifndef AppIcon
  #error "Required: /DAppIcon"
#endif
#ifndef InventoryPath
  #error "Required: /DInventoryPath"
#endif

[Setup]
AppId={{2B481502-F439-4D26-9B75-6C0D7D6D05B5}
AppName=NAP Protected Host (no activation)
AppVersion={#AppVersion}
AppVerName=NAP Protected Distribution {#AppVersion}
AppPublisher=RobDev
DefaultDirName={autopf}\NAP
DisableDirPage=yes
UsePreviousAppDir=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
SetupLogging=yes
CloseApplications=no
RestartApplications=no
OutputDir={#ReleaseOutput}
OutputBaseFilename=NAP-ProtectedSetup-{#AppVersion}
SetupIconFile={#AppIcon}
UninstallDisplayIcon={app}\NAP.App.exe
UninstallDisplayName=NAP Protected Host - binaries only
VersionInfoVersion={#AppVersion}
VersionInfoDescription=NAP09 Protected binaries (no data, writer or CUTOVER)
VersionInfoCompany=RobDev
VersionInfoProductName=Nexus Asset Platform
DisableWelcomePage=no
AllowNoIcons=yes
ChangesAssociations=no
ChangesEnvironment=no
CreateUninstallRegKey=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; The signed-off inventory is separate from the listed binaries. Launch config is
; deliberately absent; it must only be installed during a distinct authorized CUTOVER.
Source: "{#InventoryPath}"; DestDir: "{app}"; DestName: "production-inventory.json"; Flags: ignoreversion

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExpectedPath: String;
  ActualPath: String;
begin
  Result := '';
  ExpectedPath := ExpandConstant('{autopf}\NAP');
  ActualPath := ExpandConstant('{app}');
  if CompareText(RemoveBackslashUnlessRoot(ActualPath),
                 RemoveBackslashUnlessRoot(ExpectedPath)) <> 0 then
    Result := 'NAP09: destino no autorizado; no se instalara fuera de Program Files.';
end;
