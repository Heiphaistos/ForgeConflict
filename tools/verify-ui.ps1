#Requires -Version 5.1
<#
.SYNOPSIS
    Pilote l'interface de ForgeConflict par UI Automation et rapporte ce qu'elle affiche.

.DESCRIPTION
    Le script se declare DPI-aware AVANT toute lecture de coordonnees. Sans cela, UI
    Automation rend des coordonnees physiques que Windows retraduit pour un processus
    non conscient du DPI, et les clics tombent a cote : la navigation parait cassee
    alors qu'elle fonctionne.
#>
param(
    [string]$Exe = 'D:\Projet\ForgeConflict\publish\ForgeConflict.exe'
)

$ErrorActionPreference = 'Stop'

$interop = @'
using System;
using System.Runtime.InteropServices;
public static class Souris {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, int e);
    public static void Clic(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(200);
        mouse_event(0x0002, 0, 0, 0, 0);
        mouse_event(0x0004, 0, 0, 0, 0);
    }
}
'@
Add-Type -TypeDefinition $interop -Language CSharp
[Souris]::SetProcessDPIAware() | Out-Null

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$logDir = Join-Path (Split-Path $Exe) '.logs'
if (Test-Path $logDir) { Remove-Item $logDir -Recurse -Force }

$proc = Start-Process $Exe -PassThru
Write-Host "PID $($proc.Id)"

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

$win = $null
for ($i = 0; $i -lt 60 -and -not $win; $i++) {
    Start-Sleep -Milliseconds 400
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
    $win = $AE::RootElement.FindFirst($TS::Children, $cond)
}
if (-not $win) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    throw "fenetre introuvable"
}
Write-Host "Fenetre : '$($win.Current.Name)'"

function Get-ById([string]$id) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    return $win.FindFirst($TS::Descendants, $c)
}
function Text-Of([string]$id) {
    $el = Get-ById $id
    if (-not $el) { return '<absent>' }
    try { return $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch {}
    return $el.Current.Name
}
function Aller-A([string]$nom) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $nom)
    $item = $win.FindFirst($TS::Descendants, $c)
    if (-not $item) { return $false }

    $r = $item.Current.BoundingRectangle
    [Souris]::Clic([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Seconds 3
    return $true
}

foreach ($n in @('Tableau de bord', 'Inventaire', 'Journal')) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $n)
    $present = $null -ne $win.FindFirst($TS::Descendants, $c)
    Write-Host ("Navigation '{0}' : {1}" -f $n, $(if ($present) { 'present' } else { 'ABSENT' }))
}

Write-Host ""
Write-Host "--- avant analyse ---"
Write-Host ("  Etat global : " + (Text-Of 'EtatGlobal'))
Write-Host ("  Compte      : " + (Text-Of 'Compte'))

$scan = Get-ById 'ScanButton'
if (-not $scan) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    throw "bouton d'analyse introuvable"
}
$scan.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 12

Write-Host ""
Write-Host "--- apres analyse ---"
Write-Host ("  Etat global : " + (Text-Of 'EtatGlobal'))
Write-Host ("  Compte      : " + (Text-Of 'Compte'))
Write-Host ("  Machine     : " + (Text-Of 'Sous'))
Write-Host ("  Horodatage  : " + (Text-Of 'Horodatage'))

if (Aller-A 'Inventaire') {
    $rapport = Text-Of 'Rapport'
    Write-Host ""
    Write-Host "--- page Inventaire ---"
    if ($rapport -eq '<absent>') {
        Write-Host "  ECHEC : la page ne s'est pas chargee"
    } else {
        $lignes = ($rapport -split "`r?`n") | Select-Object -First 14
        $lignes | ForEach-Object { Write-Host "  $_" }
        Write-Host ("  ... ({0} caracteres au total)" -f $rapport.Length)
    }
}

if (Aller-A 'Journal') {
    $contenu = Text-Of 'Contenu'
    Write-Host ""
    Write-Host "--- page Journal ---"
    if ($contenu -eq '<absent>') {
        Write-Host "  ECHEC : la page ne s'est pas chargee"
    } else {
        Write-Host ("  {0} ligne(s) affichee(s)" -f ($contenu -split "`r?`n").Count)
    }
}

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

Write-Host ""
Write-Host "--- exceptions dans le journal ? ---"
$logFile = Join-Path $logDir 'forgeconflict.log'
if (Test-Path $logFile) {
    $err = Select-String -Path $logFile -Pattern '\[ERROR\]'
    if ($err) { $err | ForEach-Object { Write-Host ("  " + $_.Line) } } else { Write-Host "  aucune" }
} else {
    Write-Host "  journal absent"
}
