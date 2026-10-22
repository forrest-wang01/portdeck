param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$Repository,

    [Parameter(Mandatory = $true)]
    [string]$Sha256,

    [string]$Arm64Sha256 = "",

    [string]$OutputRoot = "packaging"
)

$ErrorActionPreference = "Stop"

if ($Repository -notmatch '^[^/]+/[^/]+$') {
    throw "Repository must look like owner/name."
}

$cleanVersion = $Version.TrimStart('v')
$baseUrl = "https://github.com/$Repository"
$releaseUrl = "$baseUrl/releases/download/v$cleanVersion"
$scoopDir = Join-Path $OutputRoot "scoop"
$wingetDir = Join-Path $OutputRoot "winget/PortDeck"
New-Item -ItemType Directory -Force -Path $scoopDir, $wingetDir | Out-Null

$scoop = [ordered]@{
    version     = $cleanVersion
    description = "Local TCP/UDP port and dev-service dashboard for Windows"
    homepage    = $baseUrl
    license     = "MIT"
    url         = "$releaseUrl/PortDeck-$cleanVersion-win-x64.exe"
    hash        = $Sha256.ToLowerInvariant()
    bin         = "PortDeck-$cleanVersion-win-x64.exe"
    shortcuts   = ,@("PortDeck-$cleanVersion-win-x64.exe", "PortDeck")
    checkver    = @{ github = $baseUrl }
    autoupdate  = @{
        url  = "$baseUrl/releases/download/v`$version/PortDeck-`$version-win-x64.exe"
        hash = @{
            url = "$baseUrl/releases/download/v`$version/PortDeck-`$version-win-x64.exe.sha256"
        }
    }
}

$scoop | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $scoopDir "portdeck.json") -Encoding UTF8

$versionManifest = @"
PackageIdentifier: PortDeck.PortDeck
PackageVersion: $cleanVersion
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@

$installers = @(
    @{
        Architecture  = "x64"
        InstallerUrl  = "$releaseUrl/PortDeck-$cleanVersion-win-x64.exe"
        InstallerSha256 = $Sha256.ToUpperInvariant()
    }
)

if (-not [string]::IsNullOrWhiteSpace($Arm64Sha256)) {
    $installers += @{
        Architecture  = "arm64"
        InstallerUrl  = "$releaseUrl/PortDeck-$cleanVersion-win-arm64.exe"
        InstallerSha256 = $Arm64Sha256.ToUpperInvariant()
    }
}

$lines = @(
    "PackageIdentifier: PortDeck.PortDeck",
    "PackageVersion: $cleanVersion",
    "InstallerType: portable",
    "Commands:",
    "  - portdeck",
    "Installers:"
)
foreach ($installer in $installers) {
    $lines += "  - Architecture: $($installer.Architecture)"
    $lines += "    InstallerUrl: $($installer.InstallerUrl)"
    $lines += "    InstallerSha256: $($installer.InstallerSha256)"
}
$lines += "ManifestType: installer"
$lines += "ManifestVersion: 1.6.0"
$lines | Set-Content (Join-Path $wingetDir "PortDeck.installer.yaml") -Encoding UTF8

$locale = @"
PackageIdentifier: PortDeck.PortDeck
PackageVersion: $cleanVersion
PackageLocale: en-US
Publisher: PortDeck contributors
PackageName: PortDeck
License: MIT
ShortDescription: Local TCP/UDP port and dev-service dashboard for Windows
PackageUrl: $baseUrl
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@
$locale | Set-Content (Join-Path $wingetDir "PortDeck.locale.en-US.yaml") -Encoding UTF8
$versionManifest | Set-Content (Join-Path $wingetDir "PortDeck.yaml") -Encoding UTF8

Write-Host "Wrote Scoop manifest to $scoopDir"
Write-Host "Wrote winget manifests to $wingetDir"
Write-Host "Next: winget validate $wingetDir"
