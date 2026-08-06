#Requires -Version 5.1
# Verifie que la page Apparence expose les dix themes et qu'un changement est
# reellement applique puis persiste au redemarrage.
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

$exe = 'D:\Projet\ForgeConflict\publish\ForgeConflict.exe'
$settings = 'D:\Projet\ForgeConflict\publish\forgeconflict-settings.json'
if (Test-Path $settings) { Remove-Item $settings -Force }

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

function Ouvrir {
    $p = Start-Process $exe -PassThru
    $w = $null
    for ($i = 0; $i -lt 60 -and -not $w; $i++) {
        Start-Sleep -Milliseconds 400
        $c = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
        $w = $AE::RootElement.FindFirst($TS::Children, $c)
    }
    if (-not $w) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; throw "fenetre introuvable" }
    return @{ Proc = $p; Win = $w }
}
function Cliquer($win, [string]$nom) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $nom)
    $el = $win.FindFirst($TS::Descendants, $c)
    if (-not $el) { return $false }
    $r = $el.Current.BoundingRectangle
    [Souris]::Clic([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/2))
    Start-Sleep -Seconds 2
    return $true
}

# --- Session 1 : parcourir les themes ---
$s = Ouvrir
Write-Host "PID $($s.Proc.Id)"

if (-not (Cliquer $s.Win 'Apparence')) { Stop-Process -Id $s.Proc.Id -Force; throw "page Apparence introuvable" }

$attendus = @('Graphite','Nocturne','Ardoise','Cuivre','Foret','Amethyste','Terminal','Ambre','Papier','Albatre')
$trouves = 0
foreach ($n in $attendus) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $n)
    if ($s.Win.FindFirst($TS::Descendants, $c)) { $trouves++ } else { Write-Host "  ABSENT : $n" }
}
Write-Host "Themes presents : $trouves / 10"

# Choisir Terminal, un theme visuellement tres eloigne du defaut.
if (Cliquer $s.Win 'Terminal') { Write-Host "Theme 'Terminal' selectionne" }
Start-Sleep -Seconds 2

# L'application doit rester utilisable apres bascule.
$navOk = Cliquer $s.Win 'Tableau de bord'
Write-Host "Retour au tableau de bord apres bascule : $navOk"

Stop-Process -Id $s.Proc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Write-Host ""
Write-Host "--- persistance ---"
if (Test-Path $settings) {
    Write-Host ("  fichier : " + (Get-Content $settings -Raw).Trim())
} else {
    Write-Host "  ECHEC : aucune preference enregistree"
}

# --- Session 2 : le theme doit etre retenu ---
$s2 = Ouvrir
if (Cliquer $s2.Win 'Apparence') {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'ThemeActif')
    $marque = $s2.Win.FindFirst($TS::Descendants, $c)
    Write-Host ("  marque 'actif' visible au redemarrage : " + ($null -ne $marque))
}
Stop-Process -Id $s2.Proc.Id -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "--- exceptions ---"
$log = 'D:\Projet\ForgeConflict\publish\.logs\forgeconflict.log'
if (Test-Path $log) {
    $err = Select-String -Path $log -Pattern '\[ERROR\]'
    if ($err) { $err | ForEach-Object { Write-Host ("  " + $_.Line) } } else { Write-Host "  aucune" }
    Write-Host "--- themes appliques ---"
    Select-String -Path $log -Pattern 'theme applique' | ForEach-Object { Write-Host ("  " + $_.Line) }
}
