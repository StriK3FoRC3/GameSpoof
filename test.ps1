$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\build.ps1"
Push-Location (Join-Path $PSScriptRoot 'build')
try {
    & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /r:GameSpoof.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /out:tests.exe "$PSScriptRoot\tests\tests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /r:GameSpoof.exe /r:System.Core.dll /r:System.Web.Extensions.dll /out:CatalogTests.exe "$PSScriptRoot\tests\CatalogTests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Catalog test build failed.' }
    & .\CatalogTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Catalog tests failed.' }
    & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /r:GameSpoof.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll /out:LibraryCacheTests.exe "$PSScriptRoot\tests\LibraryCacheTests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Library cache test build failed.' }
    & .\LibraryCacheTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Library cache tests failed.' }
    & .\tests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
} finally { Pop-Location }
