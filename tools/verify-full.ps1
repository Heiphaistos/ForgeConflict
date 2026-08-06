#Requires -Version 5.1
<#
.SYNOPSIS
    Verifie les sept pages de ForgeConflict par UI Automation.

.DESCRIPTION
    Lecture seule : le script navigue, lance une analyse et lit ce qui s'affiche.
    Il ne clique JAMAIS sur une action modifiant le systeme — ni correction, ni
    desinstallation.
#>
param([string]$Exe = 'D:\Projet\ForgeConflict\publish\ForgeConflict.exe')

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
        System.Threading.Thread.Sleep(250);
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

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

$proc = Start-Process $Exe -PassThru
$win = $null
for ($i = 0; $i -lt 60 -and -not $win; $i++) {
    Start-Sleep -Milliseconds 400
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
    $win = $AE::RootElement.FindFirst($TS::Children, $c)
}
if (-not $win) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue; throw "fenetre introuvable" }

function ById([string]$id) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    return $win.FindFirst($TS::Descendants, $c)
}
function TexteDe([string]$id) {
    $el = ById $id
    if (-not $el) { return '<absent>' }
    try { return $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch {}
    return $el.Current.Name
}
function Aller([string]$nom) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $nom)
    $el = $win.FindFirst($TS::Descendants, $c)
    if (-not $el) { return $false }
    $r = $el.Current.BoundingRectangle
    [Souris]::Clic([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/2))
    Start-Sleep -Seconds 3
    return $true
}

Write-Host "=== Navigation ==="
$pages = @('Tableau de bord','Conflits','Pilotes','Surveillance','Inventaire','Journal','Desinstalleur','Apparence')
foreach ($p in $pages) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $p)
    $ok = $null -ne $win.FindFirst($TS::Descendants, $c)
    Write-Host ("  {0,-18} {1}" -f $p, $(if ($ok) { 'present' } else { 'ABSENT' }))
}

Write-Host ""
Write-Host "=== Analyse ==="
(ById 'ScanButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 14
Write-Host ("  Sources : " + (TexteDe 'EtatGlobal') + " / " + (TexteDe 'Compte'))
Write-Host ("  Verdict : " + (TexteDe 'VerdictTitre'))

Write-Host ""
Write-Host "=== Page Conflits ==="
if (Aller 'Conflits') { Write-Host ("  " + (TexteDe 'Resume')) }

Write-Host ""
Write-Host "=== Page Pilotes ==="
if (Aller 'Pilotes') {
    Write-Host ("  " + (TexteDe 'Sous'))
    Write-Host ("  " + (TexteDe 'AgeCatalogue'))
    $c = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    foreach ($t in $win.FindAll($TS::Descendants, $c)) {
        $n = $t.Current.Name
        if ($n -match '^\d+\.\d+' -or $n -match 'GeForce|Radeon|UHD|Arc') {
            if ($n.Length -gt 80) { $n = $n.Substring(0,80) + '...' }
            Write-Host ("    " + $n)
        }
    }
}

Write-Host ""
Write-Host "=== Page Surveillance ==="
if (Aller 'Surveillance') { Write-Host ("  " + (TexteDe 'Etat')) }

Write-Host ""
Write-Host "=== Page Desinstalleur (lecture seule) ==="
if (Aller 'Desinstalleur') {
    $etat = TexteDe 'Etat'
    if ($etat -and $etat -ne '<absent>') { Write-Host ("  " + $etat) }
    else { Write-Host "  cibles listees, aucune action declenchee" }
}

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

Write-Host ""
Write-Host "=== Journal ==="
$log = Join-Path $logDir 'forgeconflict.log'
if (Test-Path $log) {
    $err = Select-String -Path $log -Pattern '\[ERROR\]'
    Write-Host ("  exceptions : " + $(if ($err) { $err.Count } else { 0 }))
    Select-String -Path $log -Pattern 'analyse des regles|analyse terminee' | ForEach-Object { Write-Host ("  " + $_.Line) }
}
