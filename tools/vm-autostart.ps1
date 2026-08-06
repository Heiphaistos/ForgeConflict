#Requires -Version 5.1
# Inscrit la campagne en tache planifiee et la declenche immediatement.
# ASCII strict : voir le commentaire de vm-campaign.ps1.
$ErrorActionPreference = 'Continue'

$trace = 'C:\ForgeConflict\autostart.txt'
function Note([string]$t) {
    $l = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $t
    Write-Host $l
    Add-Content -Path $trace -Value $l -Encoding UTF8
}

Set-Content -Path $trace -Value '' -Encoding UTF8
Note "Inscription de la campagne."

$cmd = 'powershell -NoProfile -ExecutionPolicy Bypass -File "C:\ForgeConflict\vm-campaign.ps1"'

# ONSTART plutot qu'ONCE : la campagne redemarre la machine, et cette tache doit
# pouvoir etre relancee sans dependre d'une heure precise.
$creation = (schtasks /create /tn "ForgeConflict-Campagne" /tr $cmd /sc onstart /ru SYSTEM /rl HIGHEST /f 2>&1 | Out-String)
Note ("Creation : " + ($creation -replace "`r?`n", ' ').Trim())

$lancement = (schtasks /run /tn "ForgeConflict-Campagne" 2>&1 | Out-String)
Note ("Lancement : " + ($lancement -replace "`r?`n", ' ').Trim())

Note "Campagne lancee en arriere-plan (compte SYSTEM)."
