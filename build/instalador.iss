; Instalador do Bordadox (Inno Setup 6). Gerado por build\publicar.ps1, que passa:
;   /DVersao=x.y.z  /DOrigem=<pasta publicada>  /DSaida=<pasta de saída>
#ifndef Versao
  #define Versao "0.0.0"
#endif
#ifndef Origem
  #error Informe /DOrigem=<pasta com o app publicado>
#endif
#ifndef Saida
  #define Saida "..\dist"
#endif

[Setup]
AppId={{EC107889-1B04-4332-96D2-D38906E2D91A}
AppName=Bordadox
AppVersion={#Versao}
AppVerName=Bordadox {#Versao}
AppPublisher=RKXP Software
AppPublisherURL=https://github.com/RKXP-Software/Bordadox
DefaultDirName={autopf}\Bordadox
DefaultGroupName=Bordadox
DisableProgramGroupPage=yes
; Instala só para o usuário atual: não pede administrador.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#Saida}
OutputBaseFilename=Bordadox-{#Versao}-setup
SetupIconFile=..\src\Bordadox.App\Assets\bordadox.ico
UninstallDisplayIcon={app}\Bordadox.App.exe
UninstallDisplayName=Bordadox
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
CloseApplications=yes
VersionInfoVersion={#Versao}

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "assocbdx"; Description: "Abrir projetos do Bordadox (.bdx) com o Bordadox"; GroupDescription: "Associação de arquivos:"
Name: "assocbordado"; Description: "Mostrar o Bordadox em ""Abrir com"" para arquivos .pes e .dst"; GroupDescription: "Associação de arquivos:"

[Files]
Source: "{#Origem}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Bordadox"; Filename: "{app}\Bordadox.App.exe"
Name: "{autodesktop}\Bordadox"; Filename: "{app}\Bordadox.App.exe"; Tasks: desktopicon

[Registry]
; Projeto .bdx: o Bordadox vira o programa padrão (somente para o usuário atual).
Root: HKA; Subkey: "Software\Classes\.bdx"; ValueType: string; ValueData: "Bordadox.Projeto"; Flags: uninsdeletekey; Tasks: assocbdx
Root: HKA; Subkey: "Software\Classes\Bordadox.Projeto"; ValueType: string; ValueData: "Projeto de bordado Bordadox"; Flags: uninsdeletekey; Tasks: assocbdx
Root: HKA; Subkey: "Software\Classes\Bordadox.Projeto\DefaultIcon"; ValueType: string; ValueData: "{app}\Bordadox.App.exe,0"; Tasks: assocbdx
Root: HKA; Subkey: "Software\Classes\Bordadox.Projeto\shell\open\command"; ValueType: string; ValueData: """{app}\Bordadox.App.exe"" ""%1"""; Tasks: assocbdx
; .pes / .dst: só aparece em "Abrir com"; não toma o lugar do programa que o usuário já usa.
Root: HKA; Subkey: "Software\Classes\Applications\Bordadox.App.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Bordadox"; Flags: uninsdeletekey; Tasks: assocbordado
Root: HKA; Subkey: "Software\Classes\Applications\Bordadox.App.exe\shell\open\command"; ValueType: string; ValueData: """{app}\Bordadox.App.exe"" ""%1"""; Tasks: assocbordado
Root: HKA; Subkey: "Software\Classes\Applications\Bordadox.App.exe\SupportedTypes"; ValueType: string; ValueName: ".pes"; ValueData: ""; Tasks: assocbordado
Root: HKA; Subkey: "Software\Classes\Applications\Bordadox.App.exe\SupportedTypes"; ValueType: string; ValueName: ".dst"; ValueData: ""; Tasks: assocbordado
Root: HKA; Subkey: "Software\Classes\.pes\OpenWithList\Bordadox.App.exe"; ValueType: none; Flags: uninsdeletekey; Tasks: assocbordado
Root: HKA; Subkey: "Software\Classes\.dst\OpenWithList\Bordadox.App.exe"; ValueType: none; Flags: uninsdeletekey; Tasks: assocbordado

[Run]
Filename: "{app}\Bordadox.App.exe"; Description: "{cm:LaunchProgram,Bordadox}"; Flags: nowait postinstall skipifsilent
