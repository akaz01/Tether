# 개발용: 소스를 Tether.exe 하나로 빌드한다. 사용자는 Tether.exe만 받으면 된다.
$ErrorActionPreference = 'Stop'
$Compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$Output = Join-Path $PSScriptRoot 'Tether.exe'
$Icon = Join-Path $PSScriptRoot 'Tether.ico'
$Sources = 'Tether.cs', 'TetherDialogs.cs', 'TetherHelp.cs', 'TetherSettings.cs', 'TetherTray.cs', 'TetherWidgets.cs', 'TetherService.cs', 'TetherPower.cs', 'TetherVersion.cs' | ForEach-Object { Join-Path $PSScriptRoot $_ }

& $Compiler /nologo /codepage:65001 /target:winexe /optimize+ "/out:$Output" "/win32icon:$Icon" /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll $Sources
if ($LASTEXITCODE -ne 0) { throw "빌드 실패: $LASTEXITCODE" }
