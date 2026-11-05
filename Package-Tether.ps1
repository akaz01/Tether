# 빌드와 규칙 시험을 거쳐 무료 배포용 ZIP을 만들고 파일 지문을 출력한다.
$ErrorActionPreference = 'Stop'
$Version = '1.0.0'
$ReleaseDir = Join-Path $PSScriptRoot 'release'
$ZipPath = Join-Path $ReleaseDir "Tether-$Version.zip"

& (Join-Path $PSScriptRoot 'Build-Tether.ps1')
pwsh -NoProfile -File (Join-Path $PSScriptRoot 'tests\Run-PolicyTests.ps1')
if ($LASTEXITCODE -ne 0) { throw "규칙 시험 실패: $LASTEXITCODE" }

New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null
$files = (Join-Path $PSScriptRoot 'Tether.exe'), (Join-Path $PSScriptRoot 'README.txt')
Compress-Archive -LiteralPath $files -DestinationPath $ZipPath -Force
Write-Output "배포 파일: $ZipPath"
Write-Output "Tether.exe SHA-256: $((Get-FileHash -LiteralPath $files[0] -Algorithm SHA256).Hash)"
Write-Output "ZIP SHA-256: $((Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash)"
