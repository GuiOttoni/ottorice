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
#define MyAppURL "https://github.com/GuiOttoni/ottorice"
#define MyAppExeName "OttoRice.exe"
#ifndef PublishDir
  #define PublishDir "..\publish\win-x64"
#endif
#define MyAppIcon "..\src\OttoRice\Assets\ottorice.ico"

[Setup]
AppId={{9F3D2C71-5B84-4E0A-8D6C-2A7E4F1B93C4}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Instalador do {#MyAppName}
VersionInfoCopyright=Copyright (C) 2026 {#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=OttoRice-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
WizardImageFile=WizardImage.bmp
WizardSmallImageFile=WizardSmallImage.bmp
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; O app foi desenhado para rodar sem elevação: todos os alvos que ele escreve
; ficam em pastas do usuário (%USERPROFILE%, %LOCALAPPDATA%). Ver RNF-03.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
SetupIconFile={#MyAppIcon}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
brazilianportuguese.AppTagline=Instale rices/temas de desktop completos no Windows a partir de um manifesto — GlazeWM, YASB, Windows Terminal e mais, com backup e rollback automáticos.
english.AppTagline=Install complete desktop rice/theme setups on Windows from a manifest — GlazeWM, YASB, Windows Terminal and more, with automatic backup and rollback.

[Messages]
WelcomeLabel2=%n%nThis will install [name/ver] on your computer.%n%n{cm:AppTagline}%n%nIt is recommended that you close all other applications before continuing.

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
