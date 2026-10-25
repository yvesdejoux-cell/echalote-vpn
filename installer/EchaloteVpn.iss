; Installateur Échalote VPN (Inno Setup 6). Compiler après scripts/fetch-deps.ps1 et scripts/publish.ps1 :
;   iscc installer\EchaloteVpn.iss
; Résultat : publish\installer\EchaloteVpn-Setup-<version>.exe

#define AppVersion "0.9.0"
#define ServiceName "EchaloteVpn"

[Setup]
AppId={{A9F7829C-9956-4119-9B8A-62EF1CBD714C}
AppName=Échalote VPN
AppVerName=Échalote VPN {#AppVersion} — Client Windows
AppVersion={#AppVersion}
AppPublisher=ptidej
AppPublisherURL=https://github.com/yvesdejoux-cell/vpn-tor
DefaultDirName={autopf}\EchaloteVpn
DefaultGroupName=Échalote VPN
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\publish\installer
OutputBaseFilename=EchaloteVpn-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
LicenseFile=..\LICENSE
WizardStyle=modern
SetupIconFile=..\src\EchaloteVpn.App\Assets\EchaloteVpn.ico
UninstallDisplayIcon={app}\app\EchaloteVpn.exe
UninstallDisplayName=Échalote VPN
CloseApplications=yes

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "autostart"; Description: "Lancer Échalote VPN (icône près de l'horloge) à l'ouverture de session"

[Files]
Source: "..\publish\EchaloteVpn\service\*"; DestDir: "{app}\service"; Flags: recursesubdirs ignoreversion
Source: "..\publish\EchaloteVpn\app\*"; DestDir: "{app}\app"; Flags: recursesubdirs ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"
Source: "THIRD-PARTY.txt"; DestDir: "{app}"

[Icons]
Name: "{group}\Échalote VPN"; Filename: "{app}\app\EchaloteVpn.exe"
Name: "{group}\Licences des composants"; Filename: "{app}\THIRD-PARTY.txt"

[Registry]
; Lancement de l'interface à l'ouverture de session, pour tous les utilisateurs.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "EchaloteVpn"; \
    ValueData: """{app}\app\EchaloteVpn.exe"" --tray"; Tasks: autostart; Flags: uninsdeletevalue
; Source « EchaloteVpn » du journal d'événements (Application), pour des messages lisibles.
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Services\EventLog\Application\EchaloteVpn"; ValueType: expandsz; \
    ValueName: "EventMessageFile"; ValueData: "%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\EventLogMessages.dll"; \
    Flags: uninsdeletekey

[Run]
Filename: "{app}\app\EchaloteVpn.exe"; Description: "Ouvrir Échalote VPN"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  ServiceName = '{#ServiceName}';

function RunHidden(const FileName, Params: String): Integer;
var
  Code: Integer;
begin
  if not Exec(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Code := -1;
  Log(Format('%s %s : code %d', [FileName, Params, Code]));
  Result := Code;
end;

{ Arrête le service s'il existe et attend qu'il soit arrêté (le service retire alors le tunnel,
  Tor et le kill switch), puis ferme l'interface. }
procedure StopEchaloteVpn();
begin
  RunHidden(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass -Command "$s = Get-Service ' + ServiceName + ' -ErrorAction SilentlyContinue; ' +
    'if ($s -and $s.Status -ne ''Stopped'') { Stop-Service ' + ServiceName + ' -Force; $s.WaitForStatus(''Stopped'', ''00:01:30'') }"');
  RunHidden(ExpandConstant('{sys}\taskkill.exe'), '/F /IM EchaloteVpn.exe');
end;

function ServiceExists(): Boolean;
begin
  { sc query renvoie 1060 quand le service n'existe pas. }
  Result := RunHidden(ExpandConstant('{sys}\sc.exe'), 'query ' + ServiceName) <> 1060;
end;

{ L'application s'est appelée TorVpn, puis OnionGuard (autres AppId) : on désinstalle ces
  versions, sinon deux services se disputeraient le tunnel. }
procedure UninstallOldVersion(const Folder: String);
var
  Uninstaller: String;
begin
  Uninstaller := ExpandConstant('{autopf}\') + Folder + '\unins000.exe';
  if FileExists(Uninstaller) then
    RunHidden(Uninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  UninstallOldVersion('OnionGuard');
  UninstallOldVersion('TorVpn');
  StopEchaloteVpn();
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  BinPath: String;
begin
  if CurStep = ssPostInstall then
  begin
    { Chemin entre guillemets dans la valeur enregistrée (\" pour sc.exe) : sans eux, un chemin
      avec espaces non protégé permettrait de lancer C:\Program.exe à la place du service. }
    BinPath := '"\"' + ExpandConstant('{app}\service\EchaloteVpn.Service.exe') + '\""';
    if ServiceExists() then
      RunHidden(ExpandConstant('{sys}\sc.exe'), 'config ' + ServiceName + ' binPath= ' + BinPath + ' start= auto obj= LocalSystem')
    else
      RunHidden(ExpandConstant('{sys}\sc.exe'), 'create ' + ServiceName + ' binPath= ' + BinPath + ' start= auto obj= LocalSystem DisplayName= "Échalote VPN"');
    RunHidden(ExpandConstant('{sys}\sc.exe'), 'description ' + ServiceName + ' "Échalote VPN, client Windows : routage Onion global, tout le trafic de Windows passe par le réseau Tor."');
    { Redémarré s'il s'arrête de façon imprévue ; le kill switch bloque le trafic en attendant. }
    RunHidden(ExpandConstant('{sys}\sc.exe'), 'failure ' + ServiceName + ' reset= 86400 actions= restart/5000/restart/10000/restart/30000');
    RunHidden(ExpandConstant('{sys}\sc.exe'), 'start ' + ServiceName);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopEchaloteVpn();
    { Retire ce qu'un plantage aurait laissé : filtres WFP, règle NRPT, profils réseau. }
    RunHidden(ExpandConstant('{app}\service\EchaloteVpn.Service.exe'), '--reset-network');
    RunHidden(ExpandConstant('{sys}\sc.exe'), 'delete ' + ServiceName);
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    if DirExists(ExpandConstant('{commonappdata}\EchaloteVpn')) and
       (UninstallSilent() or (MsgBox('Supprimer aussi les réglages et les données de Tor (' +
         ExpandConstant('{commonappdata}\EchaloteVpn') + ') ?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES)) then
      DelTree(ExpandConstant('{commonappdata}\EchaloteVpn'), True, True, True);
  end;
end;
