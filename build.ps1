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
dotnet publish src/ForgeConflict.App/ForgeConflict.App.csproj `
    -c Release -r win-x64 --self-contained true -o publish
if ($LASTEXITCODE -ne 0) { throw "echec de la publication" }

# VERIFY
$exe = Join-Path $PSScriptRoot 'publish/ForgeConflict.exe'
if (-not (Test-Path $exe)) { throw "executable introuvable : $exe" }

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
