#Requires -Version 5.1
<#
Produit publish/latest.json (+ latest.json.sig) pour l'exe deja signe Authenticode.
Ne publie RIEN : les deux fichiers sont a joindre a la release GitHub avec l'exe.
Le manifeste contient version, notes (section du CHANGELOG), URL, taille, SHA-256 ; la
signature ECDSA P-256 / SHA-256 couvre les octets exacts de latest.json (format r||s).
#>
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\publish\ForgeConflict.exe'),
    [string]$Key = 'D:\MDP\forgeconflict-updater.key',
    [string]$Changelog = (Join-Path $PSScriptRoot '..\CHANGELOG.md')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Key)) { throw "cle privee absente : $Key" }
$Exe = (Resolve-Path $Exe).Path
$item = Get-Item $Exe
$version = $item.VersionInfo.ProductVersion.Split('+')[0]

$notes = ''
if (Test-Path $Changelog) {
    $inside = $false
    $lines = foreach ($l in Get-Content $Changelog -Encoding UTF8) {
        if ($l -match '^##\s+\[?v?(\d+\.\d+\.\d+)') { $inside = ($Matches[1] -eq $version); continue }
        if ($inside) { $l }
    }
    $notes = (($lines -join "`n").Trim())
}

$sha = (Get-FileHash $Exe -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    version = $version
    notes   = $notes
    url     = "https://github.com/Heiphaistos/ForgeConflict/releases/download/v$version/ForgeConflict.exe"
    size    = $item.Length
    sha256  = $sha
}
$json = $manifest | ConvertTo-Json
$bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($json)
$outDir = Split-Path $Exe
[IO.File]::WriteAllBytes((Join-Path $outDir 'latest.json'), $bytes)

$priv = [Convert]::FromBase64String(([IO.File]::ReadAllText($Key)).Trim())
$cng = [System.Security.Cryptography.CngKey]::Import($priv, [System.Security.Cryptography.CngKeyBlobFormat]::Pkcs8PrivateBlob)
$ecdsa = New-Object System.Security.Cryptography.ECDsaCng($cng)
$ecdsa.HashAlgorithm = [System.Security.Cryptography.CngAlgorithm]::Sha256
$sig = $ecdsa.SignData($bytes)
[IO.File]::WriteAllText((Join-Path $outDir 'latest.json.sig'), [Convert]::ToBase64String($sig))
Write-Host "manifeste signe : $outDir\latest.json (v$version, $($item.Length) octets, sha256 $sha)"
