# Deploy Managed Assets Script for OpenIGI Android Port
# Builds the C# assemblies in Release mode and copies them to the Android project assets directory.

$ErrorActionPreference = "Stop"

Write-Host "Building .NET ARM64 assemblies in Release mode..." -ForegroundColor Green
dotnet publish apps\ProjectIGI.Android\ProjectIGI.Android.csproj -c Release -r android-arm64 --no-self-contained
if ($LASTEXITCODE -ne 0) {
    throw "Managed publish failed; existing Android assets were not replaced."
}

$publishDir = "apps\ProjectIGI.Android\bin\Release\net10.0\android-arm64\publish"
$targetDirs = @(
    "app\android\app\src\main\assets\managed",
    "app\src\main\assets\managed"
)

foreach ($dir in $targetDirs) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    Write-Host "Copying compiled assemblies to $dir..." -ForegroundColor Green
    Copy-Item "$publishDir\*.dll" $dir -Force
    Copy-Item "$publishDir\*.pdb" $dir -Force
}

Write-Host "Deployment complete! Binaries successfully updated in target directories." -ForegroundColor Cyan
