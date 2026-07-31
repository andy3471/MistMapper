# Creates a self-signed code-signing cert for Game Bar widget sideload,
# builds the UWP widget (Release|x64), and stages an installer folder.

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$widgetDir = Join-Path $repoRoot 'src\GameBarWidget'
$certDir = Join-Path $widgetDir 'Certificates'
$pfxPath = Join-Path $widgetDir 'MistMapper.GameBarWidget_TemporaryKey.pfx'
$cerPath = Join-Path $certDir 'MistMapper.GameBarWidget.cer'

function Find-MsBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        # Prefer an install that has UWP tooling (needed for the Game Bar widget).
        $fromUwp = & $vswhere -latest -products * `
            -requires Microsoft.VisualStudio.Workload.Universal `
            -requires Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null |
            Select-Object -First 1
        if ($fromUwp) { return $fromUwp }

        $fromMsbuild = & $vswhere -latest -products * `
            -requires Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null |
            Select-Object -First 1
        if ($fromMsbuild) { return $fromMsbuild }
    }

    $candidates = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2025\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )
    return $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

$msbuild = Find-MsBuild
if (-not $msbuild) {
    throw "MSBuild not found. Install Visual Studio 2022 with the Universal Windows Platform workload."
}
Write-Host "Using MSBuild: $msbuild"

# Prefer MSBuild that has UWP XAML targets
$vsRoot = Split-Path (Split-Path (Split-Path $msbuild))
$xamlTargets = @(
    (Join-Path $vsRoot 'MSBuild\Microsoft\WindowsXaml\v17.0\Microsoft.Windows.UI.Xaml.CSharp.targets'),
    (Join-Path $vsRoot 'Microsoft\WindowsXaml\v17.0\Microsoft.Windows.UI.Xaml.CSharp.targets'),
    'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Microsoft\WindowsXaml\v17.0\Microsoft.Windows.UI.Xaml.CSharp.targets',
    'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Microsoft\WindowsXaml\v17.0\Microsoft.Windows.UI.Xaml.CSharp.targets',
    'C:\Program Files (x86)\Microsoft Visual Studio\2022\Enterprise\MSBuild\Microsoft\WindowsXaml\v17.0\Microsoft.Windows.UI.Xaml.CSharp.targets'
)
$hasXaml = $xamlTargets | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $hasXaml) {
    Write-Warning 'UWP XAML MSBuild targets not found yet. If build fails, install the Universal Windows Platform workload in Visual Studio Installer.'
} else {
    Write-Host "Found UWP XAML targets: $hasXaml"
}

New-Item -ItemType Directory -Force -Path $certDir | Out-Null

# Create / reuse code-signing certificate (CN must match Package.appxmanifest Publisher)
$existing = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq 'CN=MistMapper' -and $_.HasPrivateKey } |
    Select-Object -First 1

if (-not $existing) {
    Write-Host 'Creating self-signed certificate CN=MistMapper...'
    $existing = New-SelfSignedCertificate `
        -Type Custom `
        -Subject 'CN=MistMapper' `
        -KeyUsage DigitalSignature `
        -FriendlyName 'MistMapper Game Bar' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
}

$thumb = $existing.Thumbprint
Write-Host "Using certificate thumbprint: $thumb"

# Export PFX (passwordless for local sideload builds) and CER
$pwd = ConvertTo-SecureString -String 'TemporaryLocalOnly!' -Force -AsPlainText
Export-PfxCertificate -Cert $existing -FilePath $pfxPath -Password $pwd | Out-Null
Export-Certificate -Cert $existing -FilePath $cerPath -Force | Out-Null

# Also trust for packaging locally
$trusted = Get-ChildItem Cert:\CurrentUser\TrustedPeople -ErrorAction SilentlyContinue |
    Where-Object { $_.Thumbprint -eq $thumb }
if (-not $trusted) {
    Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
}

# Update csproj to use password if needed - PackageCertificatePassword
# For passwordless, recreate with Exportable and empty password alternative:
# Prefer signing via /p:PackageCertificateThumbprint

Write-Host 'Restoring NuGet packages...'
& $msbuild (Join-Path $widgetDir 'MistMapper.GameBarWidget.csproj') `
    /t:Restore `
    /p:Configuration=$Configuration `
    /p:Platform=$Platform `
    /v:m
if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed' }

Write-Host 'Building and packing Game Bar widget...'
& $msbuild (Join-Path $widgetDir 'MistMapper.GameBarWidget.csproj') `
    /t:Rebuild `
    /p:Configuration=$Configuration `
    /p:Platform=$Platform `
    /p:AppxBundle=Never `
    /p:UapAppxPackageBuildMode=SideloadOnly `
    /p:AppxPackageSigningEnabled=true `
    /p:PackageCertificateThumbprint=$thumb `
    /p:PackageCertificateKeyFile=$pfxPath `
    /p:PackageCertificatePassword='TemporaryLocalOnly!' `
    /v:m
if ($LASTEXITCODE -ne 0) { throw 'Widget build/pack failed. Ensure Visual Studio UWP workload is installed.' }

$appPackages = Join-Path $widgetDir 'AppPackages'
$pkgFolder = Get-ChildItem $appPackages -Directory -ErrorAction SilentlyContinue |
    Where-Object {
        $hasMsix = @(Get-ChildItem $_.FullName -Filter *.msix -File -ErrorAction SilentlyContinue).Count -gt 0
        $hasAppx = @(Get-ChildItem $_.FullName -Filter *.appx -File -ErrorAction SilentlyContinue).Count -gt 0
        $hasMsix -or $hasAppx
    } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $pkgFolder) {
    $pkgFolder = Get-ChildItem $appPackages -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object {
            $hasMsix = @(Get-ChildItem $_.FullName -Filter *.msix -File -ErrorAction SilentlyContinue).Count -gt 0
            $hasAppx = @(Get-ChildItem $_.FullName -Filter *.appx -File -ErrorAction SilentlyContinue).Count -gt 0
            $hasMsix -or $hasAppx
        } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

if (-not $pkgFolder) {
    throw "No AppPackages output found under $appPackages"
}

$stage = Join-Path $repoRoot 'publish\GameBarWidget'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item (Join-Path $pkgFolder.FullName '*') $stage -Recurse -Force
# Add-AppDevPackage requires exactly one .cer next to the script.
Get-ChildItem $stage -Filter '*.cer' -File | Sort-Object LastWriteTime -Descending | Select-Object -Skip 1 |
    ForEach-Object { Remove-Item $_.FullName -Force }
if (-not (Get-ChildItem $stage -Filter '*.cer' -File -ErrorAction SilentlyContinue)) {
    Copy-Item $cerPath (Join-Path $stage 'MistMapper.GameBarWidget.cer') -Force
}
Copy-Item (Join-Path $widgetDir 'BundleArtifacts\Install-GameBarWidget.ps1') (Join-Path $stage 'Install-GameBarWidget.ps1') -Force
Copy-Item (Join-Path $widgetDir 'BundleArtifacts\Install-GameBarWidget.cmd') (Join-Path $stage 'Install-GameBarWidget.cmd') -Force

Write-Host ''
Write-Host "Staged installer at: $stage"
Write-Host 'Run Install-GameBarWidget.cmd as Administrator to sideload, then Win+G → Widgets → MistMapper.'
