; 音签 Melotag Windows 安装包（Inno Setup 6）
; 由 .github/workflows/release.yml 在 windows-latest 上调用：
;   ISCC.exe /DMyAppVersion=1.3.1 packaging\windows\melotag.iss

#ifndef MyAppVersion
  #define MyAppVersion "1.3.1"
#endif

#define MyAppName "音签 Melotag"
#define MyAppPublisher "zdxiaoda"
#define MyAppURL "https://github.com/zdxiaoda/Melotag"
#define MyAppExeName "Melotag.exe"

[Setup]
AppId={{8F0E6B0E-6C8E-4E5A-9B7A-2F3C1D4E5A6B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\Melotag
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\..\dist\installer-win-x64
OutputBaseFilename=Melotag-{#MyAppVersion}-win-x64-setup
SetupIconFile=..\..\src\Melotag.UI\Assets\Melotag.ico
LicenseFile=..\..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#MyAppExeName}
ShowLanguageDialog=auto

[Languages]
; 只使用 Inno Setup 自带的英文向导，避免 runner 缺少额外语言文件；
; 应用界面本身仍支持中文 / 日文 / 英文。
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\..\dist\win-x64-installer\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "README.txt"; DestDir: "{app}"; DestName: "README.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
