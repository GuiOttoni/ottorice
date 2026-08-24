; Script Inno Setup para o instalador do OttoRice
; Compilar com: ISCC.exe OttoRice.iss
; (ou "C:\Users\guiot\AppData\Local\Programs\Inno Setup 6\ISCC.exe" OttoRice.iss)
;
; Uso no CI (sobrescreve os defines abaixo):
;    ISCC.exe OttoRice.iss /DAppVersion=1.2.3 /DPublishDir=..\publish\win-x64

#define MyAppName "OttoRice"
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define MyAppPublisher "Otto"
#define MyAppExeName "OttoRice.exe"
#ifndef PublishDir
  #define PublishDir "..\publish\win-x64"
#endif
#define MyAppIcon "..\src\OttoRice\Assets\ottorice.ico"

[Setup]
AppId={{9F3D2C71-5B84-4E0A-8D6C-2A7E4F1B93C4}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=OttoRice-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; O app foi desenhado para rodar sem elevação: todos os alvos que ele escreve
; ficam em pastas do usuário (%USERPROFILE%, %LOCALAPPDATA%). Ver RNF-03.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile={#MyAppIcon}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Publish com -p:IncludeNativeLibrariesForSelfExtract=true empacota tudo dentro
; do único .exe, mas o "*" (com subpastas) fica como defesa extra caso alguma
; dependência futura volte a gerar arquivos soltos (ex: libSkiaSharp.dll) —
; sem isso, uma regressão no publish silenciosamente quebra o instalador.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
