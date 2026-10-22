$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$version = ([xml](Get-Content StickyNotes.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$publishDir = ([xml](Get-Content Properties\PublishProfiles\Portable.pubxml)).Project.PropertyGroup.PublishDir
if ($publishDir -notmatch [regex]::Escape("v$version-")) { throw "Portable.pubxml publishes to '$publishDir'; update it to match version $version." }
dotnet publish StickyNotes.csproj -p:PublishProfile=Portable -nologo
if ($LASTEXITCODE) { throw 'dotnet publish failed' }
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 is not installed: winget install JRSoftware.InnoSetup' }
& $iscc /Q "/DAppVersion=$version" installer\note.app.iss
if ($LASTEXITCODE) { throw 'Installer build failed' }
Get-Item "dist\v$version-portable\note.app.exe", "dist\note.app-$version-setup.exe" | Format-Table Name, Length, LastWriteTime
