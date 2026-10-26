$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler is required.' }
$output = Join-Path $PSScriptRoot 'build'
$runtime = Join-Path $PSScriptRoot 'GameSpoof'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location $output
try {
    & $compiler /nologo /target:exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:IconBuilder.exe "$PSScriptRoot\tools\IconBuilder.cs" "$PSScriptRoot\src\Theme.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Icon build failed.' }
    & .\IconBuilder.exe
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
    $sources = @(Get-ChildItem -LiteralPath "$PSScriptRoot\src" -Filter '*.cs' | Select-Object -ExpandProperty FullName)
    & $compiler /nologo /target:winexe /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll /win32icon:app.ico /out:GameSpoof.exe $sources
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Copy-Item -LiteralPath .\GameSpoof.exe -Destination $runtime -Force
    Write-Output 'Built GameSpoof\GameSpoof.exe. Game library and playlist preserved.'
} finally { Pop-Location }
