; Script generated for Lyrow Windows Installer
; Inno Setup 6.x

#define MyAppName "Lyrow"
#define MyAppVersion "3.5"
#define MyAppPublisher "Lyrow Team"
#define MyAppURL "https://github.com/owergungor/Lyrow"
#define MyAppExeName "Lyrow.App.exe"
#define AppMutex "Lyrow_SingleInstance_f84b12c9-94b1-4c4f-9e63-47a61d15c89f"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
AppId={{E4A8812D-981D-460B-885E-53744D6C42FA}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\dist
OutputBaseFilename=Lyrow-{#MyAppVersion}-Setup-x64
SetupIconFile=..\src\Lyrow.App\Assets\lyrow.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=no
AppMutex={#AppMutex}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
english.KeepSettingsPrompt=Would you like to keep your personalized Lyrow settings and lyrics cache?
turkish.KeepSettingsPrompt=Kişiselleştirilmiş Lyrow ayarlarınızı ve şarkı sözü önbelleğinizi saklamak ister misiniz?
french.KeepSettingsPrompt=Souhaitez-vous conserver vos paramètres personnalisés et le cache des paroles ?
spanish.KeepSettingsPrompt=¿Desea conservar sus configuraciones personalizadas y la caché de letras?
german.KeepSettingsPrompt=Möchten Sie Ihre personalisierten Lyrow-Einstellungen und den Songtext-Cache behalten?

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Lyrow"; Flags: uninsdeletevalue

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
  KeepSettings: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\Lyrow');
    if DirExists(DataDir) then
    begin
      KeepSettings := MsgBox(CustomMessage('KeepSettingsPrompt'), mbConfirmation, MB_YESNO);
      if KeepSettings = IDNO then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
