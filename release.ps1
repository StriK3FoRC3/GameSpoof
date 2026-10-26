param([string]$Version = '1.0.0')

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must use the format 1.0.0.' }
& "$PSScriptRoot\build.ps1"

$distribution = Join-Path $PSScriptRoot 'dist'
$staging = Join-Path $PSScriptRoot ('build\release-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $staging 'GameSpoof'
New-Item -ItemType Directory -Path $package, $distribution -Force | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot\build\GameSpoof.exe" -Destination $package
Copy-Item -LiteralPath "$PSScriptRoot\README.md" -Destination $package
$archive = Join-Path $distribution "GameSpoof-$Version-Windows.zip"
Compress-Archive -LiteralPath $package -DestinationPath $archive -Force
Write-Output "Release package: $archive"
