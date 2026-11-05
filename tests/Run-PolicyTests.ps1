# ConnectionPolicy 규칙 시험을 빌드해 실행한다.
$ErrorActionPreference = 'Stop'
$Compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$Output = Join-Path ([IO.Path]::GetTempPath()) 'TetherPolicyTests.exe'
$Sources = (Join-Path $PSScriptRoot 'PolicyTests.cs'), (Join-Path $PSScriptRoot '..\TetherService.cs'), (Join-Path $PSScriptRoot '..\TetherPower.cs')

& $Compiler /nologo /codepage:65001 /target:exe "/out:$Output" /main:PolicyTests /r:System.Core.dll /r:System.Windows.Forms.dll $Sources
if ($LASTEXITCODE -ne 0) { throw "시험 빌드 실패: $LASTEXITCODE" }
& $Output
exit $LASTEXITCODE
