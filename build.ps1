#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# KILL : une instance en cours verrouille l'executable.
Get-Process -Name 'ForgeConflict' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "arret du processus $($_.Id)"
    Stop-Process -Id $_.Id -Force -Confirm:$false
}

# CLEAN
foreach ($d in @('publish', 'src/ForgeConflict.App/bin', 'src/ForgeConflict.App/obj')) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
}

# BUILD
# Le RuntimeIdentifier reste en ligne de commande : le placer dans le csproj
# ajouterait un niveau win-x64 aux chemins de build classiques et ferait
# diverger les arbres d'artefacts.
# ErrorActionPreference repasse a Continue autour de l'appel natif : sous
# PowerShell 5.1, avec 'Stop', la moindre ligne ecrite par dotnet sur stderr
# devient une erreur TERMINANTE (NativeCommandError) et le script s'arrete
# alors que la publication a reussi -- le test sur $LASTEXITCODE juste en
# dessous n'etait jamais atteint. Le vrai verdict, c'est le code de sortie.
$prefPrecedente = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
dotnet publish src/ForgeConflict.App/ForgeConflict.App.csproj `
    -c Release -r win-x64 --self-contained true -o publish
$codePublication = $LASTEXITCODE
$ErrorActionPreference = $prefPrecedente
if ($codePublication -ne 0) { throw "echec de la publication (code $codePublication)" }

# VERIFY
$exe = Join-Path $PSScriptRoot 'publish/ForgeConflict.exe'
if (-not (Test-Path $exe)) { throw "executable introuvable : $exe" }

# SIGN : signature Authenticode (SHA-256, horodatee) avec le certificat
# Heiphaistos, s'il est present sur cette machine. L'outil et l'empreinte
# restent HORS du depot : une empreinte codee en dur ferait echouer la
# compilation de quiconque ne possede pas ce certificat, CI comprise. Absent,
# l'etape est simplement sautee et la publication sort non signee.
# Voir D:\Projet\outils\signer\LISEZMOI.md
$signer = 'D:\Projet\outils\signer\signer.ps1'
if (Test-Path $signer) {
    & $signer $exe
} else {
    Write-Host "certificat de signature absent de cette machine - publication non signee"
}

$item = Get-Item $exe
# ProductVersion porte le hash git en suffixe : on n'affiche que la version.
$version = $item.VersionInfo.ProductVersion.Split('+')[0]
$sizeMb = [math]::Round($item.Length / 1MB, 1)
$loose = @(Get-ChildItem publish -File | Where-Object { $_.Name -ne 'ForgeConflict.exe' })

Write-Host ""
Write-Host "ForgeConflict $version" -ForegroundColor Green
Write-Host "  $exe"
Write-Host "  $sizeMb Mo, $($loose.Count) fichier(s) annexe(s)"
if ($loose.Count -gt 0) {
    $loose | ForEach-Object { Write-Host "    - $($_.Name)" }
    Write-Warning "la publication n'est pas un fichier unique"
}
