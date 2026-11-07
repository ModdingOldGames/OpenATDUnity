; OpenAT - Windows setup wizard (Inno Setup 6).
; Packs a release player (built by tools/build/build-player.sh, no game content). The wizard asks for the
; user's own Airline Tycoon Deluxe folder (it fills in GOG and Steam installs it finds), checks it, and copies
; the game's data folders into {app}\Game. OpenAT looks for the original game's files in a Game folder next to
; OpenAT.exe, so the copy keeps working if the original install is moved or removed. The original install is
; only read, never changed.
;
; Build: tools/installer/build-installer.ps1, or
;   ISCC.exe /DAppVersion=0.1.0 /DFlavor=enhanced /DPlayerDir=<player folder> /DOutputDir=<folder> tools\installer\OpenAT.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef Flavor
  #define Flavor "enhanced"
#endif
#ifndef PlayerDir
  #error Pass /DPlayerDir=<folder with OpenAT.exe>
#endif
#ifndef OutputDir
  #define OutputDir "..\..\Builds\Installer"
#endif

#if Flavor == "faithful"
  #define AppName "OpenAT Faithful"
  #define AppIdGuid "{{3B8E5D21-7F4A-4C2E-9A61-2D7C0B9E4F13}"
  #define SetupName "OpenAT-Faithful-Setup-" + AppVersion
#else
  #define AppName "OpenAT"
  #define AppIdGuid "{{A4C7F0D2-1E6B-4B39-8D52-6F0E3A9C7B48}"
  #define SetupName "OpenAT-Setup-" + AppVersion
#endif
#define AppExe "OpenAT.exe"

[Setup]
AppId={#AppIdGuid}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=OpenAT contributors
AppPublisherURL=https://github.com/ModdingOldGames/OpenATDUnity
AppComments=Open-source reimplementation of Airline Tycoon Deluxe. Requires your own copy of the game.
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; per-user install by default (no admin needed); the user may choose an all-users install
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename={#SetupName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
InfoBeforeFile=INSTALL_NOTICE.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PlayerDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "Game\*,*_BurstDebugInformation_DoNotShip\*"
; The game's data, copied from the user's own install at install time (removed again on uninstall).
Source: "{code:GameDir}\data\*"; DestDir: "{app}\Game\data"; Flags: external recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{code:GameDir}\gli\*"; DestDir: "{app}\Game\gli"; Flags: external recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{code:GameDir}\room\*"; DestDir: "{app}\Game\room"; Flags: external recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{code:GameDir}\misc\*"; DestDir: "{app}\Game\misc"; Flags: external recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{code:GameDir}\intro\*"; DestDir: "{app}\Game\intro"; Flags: external recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{code:GameDir}\video\*"; DestDir: "{app}\Game\video"; Flags: external recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{code:GameDir}\SOUND\*"; DestDir: "{app}\Game\SOUND"; Flags: external recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{code:GameDir}\VOICE\*"; DestDir: "{app}\Game\VOICE"; Flags: external recursesubdirs createallsubdirs skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  GamePage: TInputDirWizardPage;

const
  GogKey = 'SOFTWARE\GOG.com\Games\1426519399';
  SteamKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 331920';

{ The folders OpenAT reads; the install itself (At.exe etc.) is not needed. }
function IsGameInstall(Dir: String): Boolean;
begin
  Dir := RemoveBackslashUnlessRoot(Dir);
  Result := (Dir <> '') and DirExists(Dir + '\data') and DirExists(Dir + '\gli') and DirExists(Dir + '\room')
    and FileExists(Dir + '\data\city.csv');
end;

function FromRegistry(Key, Value: String; var Dir: String): Boolean;
begin
  Result := (RegQueryStringValue(HKLM32, Key, Value, Dir) or RegQueryStringValue(HKLM64, Key, Value, Dir)
    or RegQueryStringValue(HKCU, Key, Value, Dir)) and IsGameInstall(Dir);
end;

{ Default folder: /GAMEDIR=, the GOG and Steam registry entries, then the usual install places. }
function FindGame(): String;
var
  P: String;
  Candidates: TArrayOfString;
  I: Integer;
begin
  Result := ExpandConstant('{param:GAMEDIR|}');
  if IsGameInstall(Result) then Exit;
  if FromRegistry(GogKey, 'path', P) then begin Result := P; Exit; end;
  if FromRegistry(SteamKey, 'InstallLocation', P) then begin Result := P; Exit; end;
  SetArrayLength(Candidates, 8);
  Candidates[0] := ExpandConstant('{commonpf32}\GOG Galaxy\Games\Airline Tycoon Deluxe');
  Candidates[1] := ExpandConstant('{commonpf32}\GOG.com\Airline Tycoon Deluxe');
  Candidates[2] := ExpandConstant('{commonpf32}\GOG Games\Airline Tycoon Deluxe');
  Candidates[3] := 'C:\GOG Games\Airline Tycoon Deluxe';
  Candidates[4] := ExpandConstant('{commonpf32}\Steam\steamapps\common\Airline Tycoon Deluxe');
  Candidates[5] := ExpandConstant('{commonpf}\Steam\steamapps\common\Airline Tycoon Deluxe');
  Candidates[6] := ExpandConstant('{commonpf32}\Airline Tycoon Deluxe');
  Candidates[7] := ExpandConstant('{commonpf32}\Spellbound\Airline Tycoon Deluxe');
  for I := 0 to GetArrayLength(Candidates) - 1 do
    if IsGameInstall(Candidates[I]) then begin Result := Candidates[I]; Exit; end;
  Result := '';
end;

function GameDir(Param: String): String;
begin
  Result := RemoveBackslashUnlessRoot(GamePage.Values[0]);
end;

procedure InitializeWizard();
var
  Found: String;
  Hint: String;
begin
  Found := FindGame();
  if Found <> '' then
    Hint := 'Setup found an installed copy at the folder below. Change it if you want to use another copy.'
  else
    Hint := 'Setup did not find an installed copy. Click Browse and select the folder of your Airline Tycoon Deluxe install.';
  GamePage := CreateInputDirPage(wpSelectDir,
    'Airline Tycoon Deluxe',
    'Where is your copy of Airline Tycoon Deluxe installed?',
    '{#AppName} uses the graphics, sounds, voices, videos, texts and tables of your own copy of the game. ' +
    'Setup copies them (about 550 MB) from this folder into the {#AppName} folder. Your Airline Tycoon Deluxe install is not changed.' + #13#10#13#10 +
    Hint + #13#10 + 'The folder contains At.exe and the data, gli and room folders.',
    False, '');
  GamePage.Add('');
  GamePage.Values[0] := Found;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = GamePage.ID then
  begin
    if not IsGameInstall(GamePage.Values[0]) then
    begin
      MsgBox('This folder does not contain an Airline Tycoon Deluxe install' + #13#10 +
        '(the data, gli and room folders and data\city.csv are needed).', mbError, MB_OK);
      Result := False;
    end
    else if CompareText(RemoveBackslashUnlessRoot(GamePage.Values[0]), RemoveBackslashUnlessRoot(WizardDirValue)) = 0 then
    begin
      MsgBox('Please install {#AppName} into a different folder than Airline Tycoon Deluxe.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo,
  MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine + 'Game data copied from:' + NewLine + Space + GamePage.Values[0];
  if MemoTasksInfo <> '' then Result := Result + NewLine + NewLine + MemoTasksInfo;
end;
