param(
    [Parameter(Mandatory = $true)]
    [string]$CertificatePath,

    [string]$FilePath = "dist\PortDeck.exe",

    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $CertificatePath)) {
    throw "Certificate not found: $CertificatePath"
}

if (-not (Test-Path -LiteralPath $FilePath)) {
    throw "Executable not found: $FilePath"
}

$password = $env:PORTDECK_SIGNING_PASSWORD

if ([string]::IsNullOrEmpty($password)) {
    $secure = Read-Host "PFX password" -AsSecureString
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
    }
}

$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" `
    -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (-not $signtool) {
    throw "signtool.exe was not found. Install the Windows SDK signing tools."
}

& $signtool.FullName sign `
    /f $CertificatePath `
    /p $password `
    /fd SHA256 `
    /tr $TimestampUrl `
    /td SHA256 `
    $FilePath

if ($LASTEXITCODE -ne 0) {
    throw "Signing failed with exit code $LASTEXITCODE."
}

& $signtool.FullName verify /pa /v $FilePath

if ($LASTEXITCODE -ne 0) {
    throw "Signature verification failed with exit code $LASTEXITCODE."
}

Write-Host "Signed and verified: $FilePath"
