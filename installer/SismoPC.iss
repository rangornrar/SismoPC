; Installeur SismoPC (Inno Setup 6) — ADR-8
; Compilé par GitHub Actions : iscc /DAppVersion=0.1.0 /DPublishDir=..\publish installer\SismoPC.iss
;
; Installe :
;   - le service Windows « SismoPC » (collecte, compte système, démarrage automatique, redémarrage en cas d'arrêt) ;
;   - SismoPC.Notifier, lancé à chaque ouverture de session (notifications) ;
;   - l'interface SismoPC, avec un raccourci du menu Démarrer portant l'identifiant rangornrar.SismoPC
;     (indispensable aux notifications Windows) ;
;   - le protocole sismopc:// (un clic sur une notification ouvre le détail de l'épisode).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define AppName "SismoPC"
#define AppPublisher "Patrick (rangornrar)"
#define AppUrl "https://github.com/rangornrar/SismoPC"
#define AppUserModelId "rangornrar.SismoPC"
#define ServiceName "SismoPC"

[Setup]
AppId={{61C00F5C-1B46-43DD-A8E8-2FE46414524E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts
OutputBaseFilename=SismoPC-{#AppVersion}-setup-x64
SetupIconFile=..\assets\SismoPC.ico
UninstallDisplayIcon={app}\App\SismoPC.exe
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Installeur de {#AppName}

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "pawnio"; Description: "Ouvrir la page de téléchargement du pilote PawnIO (nécessaire pour lire les températures)"; Flags: unchecked

[Dirs]
; Données du service. Les utilisateurs lisent la base (interface en lecture seule) ; seul le service écrit.
Name: "{commonappdata}\{#AppName}"; Permissions: users-readexec

[Files]
Source: "service-control.ps1"; Flags: dontcopy
Source: "{#PublishDir}\service\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\app\*"; DestDir: "{app}\App"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\MPL-2.0.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\App\SismoPC.exe"; AppUserModelID: "{#AppUserModelId}"; Comment: "Santé et performances du PC"

[Registry]
; Notifications : lancées à chaque ouverture de session, pour tous les utilisateurs.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SismoPC.Notifier"; ValueData: """{app}\Service\SismoPC.Notifier.exe"""; Flags: uninsdeletevalue
; Protocole sismopc:// : ouvre l'interface sur un épisode.
Root: HKCR; Subkey: "sismopc"; ValueType: string; ValueName: ""; ValueData: "URL:SismoPC"; Flags: uninsdeletekey
Root: HKCR; Subkey: "sismopc"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCR; Subkey: "sismopc\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\App\SismoPC.exe"",0"
Root: HKCR; Subkey: "sismopc\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\App\SismoPC.exe"" ""%1"""

[Run]
; Service : création, description, redémarrage automatique en cas d'arrêt, démarrage.
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""\""{app}\Service\SismoPC.Service.exe\"""" start= delayed-auto DisplayName= ""SismoPC"""; Flags: runhidden waituntilterminated; StatusMsg: "Installation du service SismoPC…"
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Enregistre l'activité et la santé du PC pour SismoPC."""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/300000"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden waituntilterminated; StatusMsg: "Démarrage du service SismoPC…"; AfterInstall: VerifyService
; Notifications dès maintenant, dans la session de l'utilisateur qui installe.
Filename: "{app}\Service\SismoPC.Notifier.exe"; Flags: nowait runasoriginaluser
Filename: "https://github.com/namazso/PawnIO.Setup/releases"; Flags: shellexec runasoriginaluser nowait; Tasks: pawnio
Filename: "{app}\App\SismoPC.exe"; Description: "Ouvrir SismoPC"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM SismoPC.Notifier.exe"; Flags: runhidden; RunOnceId: "KillNotifier"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM SismoPC.exe"; Flags: runhidden; RunOnceId: "KillApp"
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteService"

[Code]
{ Avant une mise à jour : arrête le service et les programmes, puis supprime le service pour le recréer proprement. }
function ServiceControl(Action: String): Boolean;
var
  Code: Integer;
begin
  ExtractTemporaryFile('service-control.ps1');
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\service-control.ps1') + '" -Action ' + Action,
    '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := Result and (Code = 0);
end;

procedure VerifyService;
begin
  if not ServiceControl('verify') then
    RaiseException('Le service SismoPC ne répond pas. Consulter les journaux dans ProgramData\SismoPC\logs et relancer l''installation.');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Result := '';
  if not ServiceControl('stop') then
  begin
    Result := 'Impossible d''arrêter ou supprimer le service. Fermer les outils Services Windows puis réessayer ; redémarrer Windows si nécessaire.';
    Exit;
  end;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM SismoPC.Notifier.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM SismoPC.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

{ À la désinstallation : proposer de conserver l'historique (spécifications, exigences non fonctionnelles). }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
  begin
    if MsgBox('Supprimer aussi l''historique enregistré par SismoPC (' + ExpandConstant('{commonappdata}\{#AppName}') + ') ?' + #13#10 +
              'Choisis « Non » pour le conserver en vue d''une réinstallation.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{commonappdata}\{#AppName}'), True, True, True);
  end;
end;
