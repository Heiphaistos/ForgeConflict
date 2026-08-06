#Requires -Version 5.1
<#
.SYNOPSIS
    Lit un fichier de l'invite en montant son disque hors-ligne, sans identifiants.

.DESCRIPTION
    Canal de secours quand KVP ne rapporte rien. La VM est mise en etat sauvegarde
    (l'etat exact est preserve), son disque est monte en lecture seule sur l'hote,
    les fichiers demandes sont copies, puis la VM est relancee.

    Lecture seule de bout en bout : ce script ne modifie jamais le contenu de l'invite.

.EXAMPLE
    .\read-vm-report.ps1 -Paths 'ForgeConflict\rapport.txt','ForgeConflict\.logs\forgeconflict.log'
#>
param(
    [string]$VMName = 'W11 dev',
    [string[]]$Paths = @('ForgeConflict\rapport.txt', 'ForgeConflict\.logs\forgeconflict.log'),
    [string]$OutputDirectory = 'docs\verification'
)

$ErrorActionPreference = 'Stop'

$vm = Get-VM -Name $VMName
$wasRunning = $vm.State -eq 'Running'
$disk = (Get-VMHardDiskDrive -VMName $VMName).Path
$mounted = $false

try {
    if ($wasRunning) {
        Write-Host "mise en etat sauvegarde de '$VMName'..."
        Save-VM -Name $VMName
        Start-Sleep -Seconds 3
    }

    Mount-VHD -Path $disk -ReadOnly
    $mounted = $true
    Start-Sleep -Seconds 3

    # Identifier la lettre attribuee : c'est le volume portant \Windows.
    $letter = $null
    foreach ($l in [char[]](68..90)) {
        if (Test-Path "${l}:\Windows\System32\config\SOFTWARE") { $letter = $l; break }
    }
    if (-not $letter) { throw "volume Windows introuvable apres montage" }
    Write-Host "invite monte sur ${letter}:"

    if (-not (Test-Path $OutputDirectory)) {
        New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    }

    $stamp = Get-Date -Format 'yyyy-MM-dd-HHmmss'
    foreach ($p in $Paths) {
        $src = "${letter}:\$p"
        if (Test-Path $src) {
            $name = "$stamp-" + (Split-Path $p -Leaf)
            Copy-Item $src (Join-Path $OutputDirectory $name) -Force
            Write-Host "  recupere : $p"
            Write-Host ("  --- contenu ---")
            Get-Content $src -Encoding UTF8 | ForEach-Object { Write-Host "  $_" }
        } else {
            Write-Warning "  absent : $p"
        }
    }
}
finally {
    if ($mounted) {
        Dismount-VHD -Path $disk -ErrorAction SilentlyContinue
        Write-Host "disque demonte"
    }
    if ($wasRunning) {
        Start-VM -Name $VMName -ErrorAction SilentlyContinue
        Write-Host "'$VMName' relancee"
    }
}
