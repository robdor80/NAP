; NAP06 - laboratorio Windows x64. No servicio, CUTOVER ni migracion de datos.
#ifndef AppVersion
  #error "Es obligatorio definir /DAppVersion=2.0.0"
#endif
#ifndef PayloadDir
  #error "Es obligatorio definir /DPayloadDir=<ruta>"
#endif
#ifndef ReleaseOutput
  #error "Es obligatorio definir /DReleaseOutput=<ruta>"
#endif
#ifndef AppIcon
  #error "Es obligatorio definir /DAppIcon=<ruta>"
#endif

[Setup]
AppId={{B2F9E5B8-9D72-4DB2-964E-7E3D3E3F5F68}
AppName=NAP - Nexus Asset Platform
AppVersion={#AppVersion}
AppVerName=NAP {#AppVersion}
AppPublisher=RobDev
DefaultDirName={localappdata}\Programs\NAP
DisableDirPage=yes
UsePreviousAppDir=no
DefaultGroupName=NAP
PrivilegesRequired=lowest
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
OutputBaseFilename=NAP-Setup-{#AppVersion}
SetupIconFile={#AppIcon}
UninstallDisplayIcon={app}\NAP.App.exe
UninstallDisplayName=NAP - Nexus Asset Platform
VersionInfoVersion={#AppVersion}
VersionInfoDescription=NAP Installer (laboratorio NAP06)
VersionInfoCompany=RobDev
VersionInfoProductName=Nexus Asset Platform
DisableWelcomePage=no
AllowNoIcons=yes
ChangesAssociations=no
ChangesEnvironment=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\NAP - Nexus Asset Platform"; Filename: "{app}\NAP.App.exe"; WorkingDir: "{app}"
Name: "{group}\Desinstalar NAP"; Filename: "{uninstallexe}"
Name: "{autodesktop}\NAP"; Filename: "{app}\NAP.App.exe"; Tasks: desktopicon; WorkingDir: "{app}"

[Code]
// Prohibir /DIR u otra redireccion hacia catalogos, datos, maestros o repos Git.
// Directorio fijo, por usuario y separado de %LOCALAPPDATA%\NAP.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExpectedPath: String;
  ActualPath: String;
begin
  Result := '';
  ExpectedPath := ExpandConstant('{localappdata}\Programs\NAP');
  ActualPath := ExpandConstant('{app}');
  if CompareText(RemoveBackslashUnlessRoot(ActualPath),
                 RemoveBackslashUnlessRoot(ExpectedPath)) <> 0 then
    Result := 'Directorio de instalacion no autorizado para NAP06. No se modificaran archivos.';
end;
