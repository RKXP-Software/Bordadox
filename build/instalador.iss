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
; Atualização: o mesmo AppId faz a nova versão instalar por cima da antiga, na mesma pasta e com as mesmas opções.
UsePreviousAppDir=yes
UsePreviousTasks=yes
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=yes
VersionInfoVersion={#Versao}

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "assocbdx"; Description: "Abrir projetos do Bordadox (.bdx) com o Bordadox"; GroupDescription: "Associação de arquivos:"
Name: "assocbordado"; Description: "Mostrar o Bordadox em ""Abrir com"" para arquivos .pes, .dst, .exp, .jef e .vp3"; GroupDescription: "Associação de arquivos:"

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
; .pes, .dst, .exp, .jef, .vp3: só aparece em "Abrir com"; não toma o lugar do programa que o usuário já usa.
Root: HKA; Subkey: "Software\Classes\Applications\Bordadox.App.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Bordadox"; Flags: uninsdeletekey; Tasks: assocbordado
Root: HKA; Subkey: "Software\Classes\Applications\Bordadox.App.exe\shell\open\command"; ValueType: string; ValueData: """{app}\Bordadox.App.exe"" ""%1"""; Tasks: assocbordado
Root: HKA; Subkey: "Software\Classes\Applications\Bordadox.App.exe\SupportedTypes"; ValueType: string; ValueName: ".pes"; ValueData: ""; Tasks: assocbordado
Root: HKA; Subkey: "Software\Classes\Applications\Bordadox.App.exe\SupportedTypes"; ValueType: string; ValueName: ".dst"; ValueData: ""; Tasks: assocbordado
Root: HKA; Subkey: "Software\Classes\Applications\Bordadox.App.exe\SupportedTypes"; ValueType: string; ValueName: ".exp"; ValueData: ""; Tasks: assocbordado
Root: HKA; Subkey: "Software\Classes\Applications\Bordadox.App.exe\SupportedTypes"; ValueType: string; ValueName: ".jef"; ValueData: ""; Tasks: assocbordado
Root: HKA; Subkey: "Software\Classes\Applications\Bordadox.App.exe\SupportedTypes"; ValueType: string; ValueName: ".vp3"; ValueData: ""; Tasks: assocbordado
Root: HKA; Subkey: "Software\Classes\.pes\OpenWithList\Bordadox.App.exe"; ValueType: none; Flags: uninsdeletekey; Tasks: assocbordado
Root: HKA; Subkey: "Software\Classes\.dst\OpenWithList\Bordadox.App.exe"; ValueType: none; Flags: uninsdeletekey; Tasks: assocbordado
Root: HKA; Subkey: "Software\Classes\.exp\OpenWithList\Bordadox.App.exe"; ValueType: none; Flags: uninsdeletekey; Tasks: assocbordado
Root: HKA; Subkey: "Software\Classes\.jef\OpenWithList\Bordadox.App.exe"; ValueType: none; Flags: uninsdeletekey; Tasks: assocbordado
Root: HKA; Subkey: "Software\Classes\.vp3\OpenWithList\Bordadox.App.exe"; ValueType: none; Flags: uninsdeletekey; Tasks: assocbordado

[Run]
Filename: "{app}\Bordadox.App.exe"; Description: "{cm:LaunchProgram,Bordadox}"; Flags: nowait postinstall skipifsilent

[InstallDelete]
; Na atualização, tira o que sobrou de versões anteriores (bibliotecas renomeadas ou removidas) antes de copiar os arquivos novos.
; Só mexe em tipos de arquivo do próprio programa, nunca na pasta inteira.
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.deps.json"
Type: files; Name: "{app}\*.runtimeconfig.json"
Type: files; Name: "{app}\LEIA-ME.txt"

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{EC107889-1B04-4332-96D2-D38906E2D91A}_is1';

var
  InstalledVersion: String;

// Versão já instalada (por usuário ou para todos), ou vazio se não há instalação.
function ReadInstalledVersion(): String;
begin
  Result := '';
  if not RegQueryStringValue(HKCU, UninstallKey, 'DisplayVersion', Result) then
    if not RegQueryStringValue(HKLM, UninstallKey, 'DisplayVersion', Result) then
      Result := '';
end;

function InitializeSetup(): Boolean;
var
  Old, New: Int64;
begin
  Result := True;
  InstalledVersion := ReadInstalledVersion();
  if InstalledVersion = '' then Exit;
  if StrToVersion(InstalledVersion, Old) and StrToVersion('{#Versao}', New) then
    if ComparePackedVersion(Old, New) > 0 then
    begin
      // A versão instalada é mais nova que este instalador: não volta atrás sem o usuário confirmar (e nunca no modo silencioso).
      if WizardSilent then
        Result := False
      else
        Result := MsgBox('O Bordadox ' + InstalledVersion + ' já está instalado, e é mais novo que esta versão ({#Versao}).' + #13#10 + #13#10 +
                         'Deseja instalar a versão mais antiga por cima mesmo assim?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
    end;
end;

// Na tela "Pronto para instalar", avisa que é uma atualização.
function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := '';
  if InstalledVersion <> '' then
    Result := 'Atualização do Bordadox da versão ' + InstalledVersion + ' para a {#Versao}.' + NewLine +
              'Seus projetos e preferências não são alterados.' + NewLine + NewLine;
  if MemoDirInfo <> '' then Result := Result + MemoDirInfo + NewLine + NewLine;
  if MemoTasksInfo <> '' then Result := Result + MemoTasksInfo;
end;
