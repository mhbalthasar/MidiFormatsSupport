param(
    [switch]$Install,
    [string]$TuneLabSdkRoot = $env:TUNELAB_SDK_ROOT
)

$ErrorActionPreference = "Stop"
# this script lives in tools/; the project root is its parent
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if ([string]::IsNullOrWhiteSpace($TuneLabSdkRoot)) {
    $TuneLabSdkRoot = Join-Path $root "..\TunelabExpermental"
}
$project = Join-Path $root "MidiFormatsSupport.csproj"
$outDir = Join-Path $root "bin\Release"
$distDir = Join-Path $root "packages"
$packageName = "MidiFormatsSupport"
$stage = Join-Path $distDir $packageName

Write-Host "Building Release..."
dotnet build $project -c Release "-p:TuneLabSdkRoot=$TuneLabSdkRoot"
if ($LASTEXITCODE -ne 0) { throw "build failed" }

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item (Join-Path $outDir "manifest.json") $stage
Copy-Item (Join-Path $outDir "Introduction.md") $stage
Get-ChildItem -Path $outDir -Filter *.dll |
    Where-Object { $_.Name -notin @("TuneLab.SDK.dll", "TuneLab.Foundation.dll") } |
    Copy-Item -Destination $stage
Get-ChildItem -Path $outDir -Filter *.deps.json | Copy-Item -Destination $stage

$tlx = Join-Path $distDir "$packageName.tlx"
if (Test-Path $tlx) { Remove-Item $tlx -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $tlx, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host "Package: $tlx"

if ($Install) {
    $target = Join-Path $env:APPDATA "TuneLab\Extensions\$packageName"
    if (Get-Process TuneLab -ErrorAction SilentlyContinue) {
        throw "TuneLab is running. Close it before installing (it locks extension DLLs)."
    }

    # Extract to a staging folder first, then copy only the package files over the
    # existing extension folder.
    $staging = Join-Path ([System.IO.Path]::GetTempPath()) "tunelab-midiformatssupport-install"
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    [System.IO.Compression.ZipFile]::ExtractToDirectory($tlx, $staging)

    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item (Join-Path $staging "*") $target -Recurse -Force
    Remove-Item $staging -Recurse -Force

    Write-Host "Installed to: $target"
}
