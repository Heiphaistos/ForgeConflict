#Requires -Version 5.1
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
$logDir = 'D:\Projet\ForgeConflict\publish\.logs'
if (Test-Path $logDir) { Remove-Item $logDir -Recurse -Force }

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

$proc = Start-Process $exe -PassThru
$win = $null
for ($i = 0; $i -lt 60 -and -not $win; $i++) {
    Start-Sleep -Milliseconds 400
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
    $win = $AE::RootElement.FindFirst($TS::Children, $c)
}
if (-not $win) { Stop-Process -Id $proc.Id -Force; throw "fenetre introuvable" }

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
    Start-Sleep -Seconds 2
    return $true
}

(ById 'ScanButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 14

Write-Host "=== Tableau de bord ==="
Write-Host ("  Etat sources : " + (TexteDe 'EtatGlobal') + " / " + (TexteDe 'Compte'))
Write-Host ("  Verdict      : " + (TexteDe 'VerdictTitre'))
Write-Host ("  Detail       : " + (TexteDe 'VerdictDetail'))

Write-Host ""
Write-Host "=== Page Conflits ==="
if (Aller 'Conflits') {
    Write-Host ("  Resume : " + (TexteDe 'Resume'))

    # Chaque constat est un Expander : on liste leurs en-tetes.
    $c = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $textes = $win.FindAll($TS::Descendants, $c)
    Write-Host "  --- constats affiches ---"
    foreach ($t in $textes) {
        $n = $t.Current.Name
        if ($n -match '^(Critique|Eleve|Moyen|Information) · FG-') { Write-Host ("    [" + $n + "]") }
        elseif ($n -and $n.Length -gt 25 -and $n -notmatch 'Lancez|regles n|complementaire|Ce qui|Pourquoi|correction ferait') {
            if ($n.Length -gt 90) { $n = $n.Substring(0,90) + '...' }
            Write-Host ("      " + $n)
        }
    }
} else {
    Write-Host "  ECHEC : page Conflits introuvable"
}

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

Write-Host ""
Write-Host "=== Journal ==="
$log = Join-Path $logDir 'forgeconflict.log'
if (Test-Path $log) {
    Select-String -Path $log -Pattern 'analyse des regles|analyse terminee|\[ERROR\]' | ForEach-Object { Write-Host ("  " + $_.Line) }
}
