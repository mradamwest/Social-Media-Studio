#define MyAppName "Social Media Studio"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "Adam West"
#define MyAppExeName "SocialMediaStudio.exe"

[Setup]
AppId={{D9E927A8-4F2C-47C9-9D62-2E87E10E71A1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Social Media Studio
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=Social-Media-Studio-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  { Ensure the desktop application is not left running while files are removed. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#MyAppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RemoveData: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RemoveData := MsgBox(
      'Application files and shortcuts have been removed.' + #13#10 + #13#10 +
      'Do you also want to permanently delete Social Media Studio user data, including drafts, schedules, publishing history, and saved account tokens?',
      mbConfirmation, MB_YESNO);

    if RemoveData = IDYES then
    begin
      DelTree(ExpandConstant('{localappdata}\SocialMediaStudio'), True, True, True);
    end;
  end;
end;
