#Requires -Version 5.1
<#
Genere la paire de cles ECDSA P-256 qui signe le manifeste des mises a jour.
  - cle PRIVEE : D:\MDP\forgeconflict-updater.key (PKCS#8 en base64) -- JAMAIS dans le depot ;
  - cle PUBLIQUE : D:\MDP\forgeconflict-updater.key.pub (X||Y en base64), a coller dans
    src/ForgeConflict.Core/Update/UpdateVerifier.cs (constante PublicKey).
Refuse d'ecraser une cle existante : la remplacer rendrait caduques toutes les copies deja livrees.
#>
param([string]$Key = 'D:\MDP\forgeconflict-updater.key')
$ErrorActionPreference = 'Stop'
if (Test-Path $Key) { throw "la cle existe deja : $Key" }
$p = New-Object System.Security.Cryptography.CngKeyCreationParameters
$p.ExportPolicy = [System.Security.Cryptography.CngExportPolicies]::AllowPlaintextExport
$cng = [System.Security.Cryptography.CngKey]::Create([System.Security.Cryptography.CngAlgorithm]::ECDsaP256, $null, $p)
$priv = $cng.Export([System.Security.Cryptography.CngKeyBlobFormat]::Pkcs8PrivateBlob)
$pub = $cng.Export([System.Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)
# EccPublicBlob = magie (4) + longueur (4) + X (32) + Y (32)
$xy = New-Object byte[] 64
[Array]::Copy($pub, 8, $xy, 0, 64)
[IO.File]::WriteAllText($Key, [Convert]::ToBase64String($priv))
[IO.File]::WriteAllText("$Key.pub", [Convert]::ToBase64String($xy))
Write-Host "cle privee : $Key"
Write-Host "cle publique (a embarquer) : $([Convert]::ToBase64String($xy))"
