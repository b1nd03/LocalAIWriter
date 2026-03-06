[Setup]
AppName=LocalAI Writer
AppVersion=1.0.0
DefaultDirName={autopf}\LocalAIWriter
DefaultGroupName=LocalAI Writer
UninstallDisplayIcon={app}\LocalAIWriter.exe
OutputDir=..\publish\installer
OutputBaseFilename=LocalAIWriter_Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startupentry"; Description: "Start with &Windows"; GroupDescription: "Startup:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\LocalAI Writer"; Filename: "{app}\LocalAIWriter.exe"
Name: "{userdesktop}\LocalAI Writer"; Filename: "{app}\LocalAIWriter.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LocalAIWriter"; ValueData: """{app}\LocalAIWriter.exe"""; Flags: uninsdeletevalue; Tasks: startupentry

[Run]
Filename: "{app}\LocalAIWriter.exe"; Description: "Launch LocalAI Writer"; Flags: nowait postinstall skipifsilent
