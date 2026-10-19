# Builds Configurable Capacity and packs the release zip.
# Result: release\ConfigurableCapacity-<version>.zip containing ONE folder, "ConfigurableCapacity", to be extracted into BepInEx\plugins
# (so the mod ends up in BepInEx\plugins\ConfigurableCapacity\ with its icon and docs, and Apocasetter can remove it cleanly).
# Run from this folder:  powershell -ExecutionPolicy Bypass -File .\Make-Release.ps1

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

[xml]$proj = Get-Content "ConfigurableCapacity.csproj"
$version = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw "No <Version> found in ConfigurableCapacity.csproj" }

dotnet build -c Release
if ($LASTEXITCODE -ne 0) { throw "The build failed" }

$stage = "release\ConfigurableCapacity"
if (Test-Path "release") { Remove-Item "release" -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item "bin\Release\net472\ConfigurableCapacity.dll" $stage
Copy-Item "ConfigurableCapacity.png", "README.md", "LICENSE", "CHANGELOG.md" $stage

$zip = "release\ConfigurableCapacity-$version.zip"
# .NET ZipFile writes standard "/" paths inside the zip (Windows PowerShell 5.1's Compress-Archive writes "\" paths, which other tools can mishandle).
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory((Resolve-Path "release\ConfigurableCapacity").Path, (Join-Path (Get-Location) $zip), [System.IO.Compression.CompressionLevel]::Optimal, $true)
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()

Write-Host ""
Write-Host "Built  $zip"
Write-Host "SHA256 $hash"
Write-Host "Attach the zip to the GitHub release tagged v$version (the tag must equal the plugin version)."
