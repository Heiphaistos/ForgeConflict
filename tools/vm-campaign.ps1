#Requires -Version 5.1
<#
.SYNOPSIS
    Campagne de verification complete de ForgeConflict en machine virtuelle.

.DESCRIPTION
    Deroule les verifications qu'aucune machine de travail ne permet : portabilite
    sans SDK, adaptateur virtualise, reprise de transaction, et surtout le garde-fou
    du mode sans echec.

    ATTENTION : ce fichier doit rester en ASCII STRICT. PowerShell 5.1 lit un script
    sans BOM en page de code ANSI ; un seul caractere accentue ou un tiret cadratin
    suffit a casser l'analyse syntaxique, et le script ne demarre alors jamais - sans
    le moindre message.

    Le rapport est ecrit dans C:\ForgeConflict\campagne.txt a chaque etape, pour
    survivre a un redemarrage comme a un plantage.
#>
param([switch]$SafeModePhase)

$ErrorActionPreference = 'Continue'

$racine  = 'C:\ForgeConflict'
$exe     = Join-Path $racine 'ForgeConflict.exe'
$rapport = Join-Path $racine 'campagne.txt'
$journal = Join-Path $env:ProgramData 'ForgeConflict\journal'

if (-not (Test-Path $racine)) { New-Item -ItemType Directory -Path $racine -Force | Out-Null }

function Note([string]$texte) {
    $ligne = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $texte
    Write-Host $ligne
    try { Add-Content -Path $rapport -Value $ligne -Encoding UTF8 } catch { }
}
function Titre([string]$texte) {
    try { Add-Content -Path $rapport -Value '' -Encoding UTF8 } catch { }
    Note ("=== " + $texte + " ===")
}

# Tout est enveloppe : une erreur inattendue doit laisser une trace, jamais
# disparaitre en silence comme lors de la premiere tentative.
try {

# =====================================================================
#  PHASE 2 : apres redemarrage, constater l'etat du mode sans echec
# =====================================================================
if ($SafeModePhase) {
    Titre "PHASE 2 : apres redemarrage"

    $enSafeMode = [bool]$env:SAFEBOOT_OPTION
    Note ("Session en mode sans echec   : " + $enSafeMode)

    $bcd = (bcdedit /enum '{current}' | Out-String)
    $flagPresent = $bcd -match 'safeboot'
    Note ("Indicateur safeboot present  : " + $flagPresent)

    $tache = (schtasks /query /tn "ForgeConflict-SortieModeSansEchec" 2>&1 | Out-String)
    $tacheExiste = -not ($tache -match 'ERROR|ERREUR|introuvable|not exist')
    Note ("Tache de sortie encore la    : " + $tacheExiste)

    if (-not $flagPresent -and -not $tacheExiste) {
        Note "RESULTAT : SUCCES. Le garde-fou a fonctionne : l'indicateur a ete retire"
        Note "           et la tache s'est supprimee. La machine revient en mode normal."
    } elseif (-not $flagPresent) {
        Note "RESULTAT : PARTIEL. Indicateur retire, mais la tache subsiste."
        schtasks /delete /tn "ForgeConflict-SortieModeSansEchec" /f 2>&1 | Out-Null
    } else {
        Note "RESULTAT : ECHEC. L'indicateur safeboot est toujours present."
        Note "           Retrait manuel de securite en cours."
        bcdedit /deletevalue '{current}' safeboot 2>&1 | Out-Null
        schtasks /delete /tn "ForgeConflict-SortieModeSansEchec" /f 2>&1 | Out-Null
    }

    schtasks /delete /tn "ForgeConflict-CampagnePhase2" /f 2>&1 | Out-Null
    Note "CAMPAGNE TERMINEE."
    return
}

# =====================================================================
#  PHASE 1
# =====================================================================
Set-Content -Path $rapport -Value '' -Encoding UTF8
Titre "CAMPAGNE ForgeConflict v1.0.0 : PHASE 1"
Note ("Machine : " + $env:COMPUTERNAME)
Note ("OS      : " + (Get-CimInstance Win32_OperatingSystem).Caption + " build " + [Environment]::OSVersion.Version.Build)
Note ("Compte  : " + $env:USERNAME)

$eleve = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Note ("Droits administrateur : " + $eleve)
if (-not $eleve) { Note "ARRET : droits administrateur requis."; return }

# --- 1. Portabilite -------------------------------------------------
Titre "1. Portabilite"
$sdk = Get-Command dotnet -ErrorAction SilentlyContinue
if ($sdk) { Note "SDK .NET PRESENT : la preuve de portabilite est affaiblie." }
else      { Note "SDK .NET absent : preuve de portabilite valide." }

if (-not (Test-Path $exe)) { Note "ARRET : executable introuvable."; return }
Note ("Executable : " + [math]::Round((Get-Item $exe).Length/1MB,1) + " Mo")

# --- 2. Interface et analyse ----------------------------------------
Titre "2. Interface et analyse des dix sources"

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$dpi = @'
using System; using System.Runtime.InteropServices;
public static class S {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint a,uint b,uint c,int d);
    public static void Clic(int x,int y){ SetCursorPos(x,y); System.Threading.Thread.Sleep(250);
        mouse_event(0x0002,0,0,0,0); mouse_event(0x0004,0,0,0,0); }
}
'@
Add-Type -TypeDefinition $dpi -Language CSharp
[S]::SetProcessDPIAware() | Out-Null

$logDir = Join-Path $racine '.logs'
if (Test-Path $logDir) { Remove-Item $logDir -Recurse -Force -ErrorAction SilentlyContinue }

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

$proc = Start-Process $exe -PassThru
$win = $null
for ($i = 0; $i -lt 75 -and -not $win; $i++) {
    Start-Sleep -Milliseconds 400
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
    $win = $AE::RootElement.FindFirst($TS::Children, $c)
}

if (-not $win) {
    # Une tache planifiee en SYSTEM s'execute en session 0, isolee du bureau : une
    # fenetre y est invisible pour UI Automation. Ce n'est pas un defaut de
    # l'application, mais une limite du contexte d'execution.
    $session = (Get-Process -Id $PID).SessionId
    if ($session -eq 0) {
        Note "Interface non verifiable : le script tourne en session 0 (compte SYSTEM),"
        Note "qui n'a pas de bureau interactif. Le processus a demarre sans erreur."
        Note ("Processus vivant : " + (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue).HasExited))
    } else {
        Note "ECHEC : la fenetre ne s'est pas ouverte."
    }
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
} else {
    Note ("Fenetre ouverte : " + $win.Current.Name)

    function ById([string]$id) {
        $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
        return $win.FindFirst($TS::Descendants, $c)
    }
    function TexteDe([string]$id) {
        $el = ById $id
        if (-not $el) { return '<absent>' }
        try { return $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { }
        return $el.Current.Name
    }
    function Aller([string]$nom) {
        $c = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $nom)
        $el = $win.FindFirst($TS::Descendants, $c)
        if (-not $el) { return $false }
        $r = $el.Current.BoundingRectangle
        [S]::Clic([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/2))
        Start-Sleep -Seconds 3
        return $true
    }

    foreach ($p in @('Tableau de bord','Conflits','Pilotes','Surveillance','Inventaire','Journal','Desinstalleur','Apparence')) {
        $c = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $p)
        $ok = $null -ne $win.FindFirst($TS::Descendants, $c)
        Note ("  page {0,-18} {1}" -f $p, $(if ($ok) { 'presente' } else { 'ABSENTE' }))
    }

    $bouton = ById 'ScanButton'
    if ($bouton) {
        $bouton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Seconds 20
        Note ("Sources : " + (TexteDe 'EtatGlobal') + " / " + (TexteDe 'Compte'))
        Note ("Verdict : " + (TexteDe 'VerdictTitre'))
    } else {
        Note "ECHEC : bouton d'analyse introuvable."
    }

    # --- 3. Cas limite : adaptateur virtualise ----------------------
    Titre "3. Cas limite : adaptateur virtualise"
    Note "Attendu : l'adaptateur Hyper-V ne doit etre signale ni comme pilote"
    Note "          generique manquant, ni comme lien PCIe degrade."

    if (Aller 'Conflits') { Note ("Conflits : " + (TexteDe 'Resume')) }

    if (Aller 'Inventaire') {
        $r = TexteDe 'Rapport'
        foreach ($motif in @('Adaptateurs', 'PCIe', 'Evenements GPU', 'Traces de plantage')) {
            $lignes = ($r -split "`r?`n") | Where-Object { $_ -match $motif }
            foreach ($l in ($lignes | Select-Object -First 3)) { Note ("  " + $l.Trim()) }
        }
    }

    if (Aller 'Pilotes') { Note ("Conseil pilote : " + (TexteDe 'Sous')) }

    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    $log = Join-Path $logDir 'forgeconflict.log'
    if (Test-Path $log) {
        $err = @(Select-String -Path $log -Pattern '\[ERROR\]')
        Note ("Exceptions dans le journal : " + $err.Count)
        foreach ($e in ($err | Select-Object -First 3)) { Note ("  " + $e.Line) }
    }
}

# --- 4. Reprise d'une transaction interrompue -----------------------
Titre "4. Reprise d'une transaction interrompue"
New-Item -ItemType Directory -Path $journal -Force | Out-Null
$id = [Guid]::NewGuid()
$tx = '{' +
  '"Id":"' + $id + '","RuleId":"FG-CAMPAGNE","Title":"Transaction de campagne",' +
  '"Risk":"Eleve","State":"Ouverte",' +
  '"OpenedAt":"' + (Get-Date).ToString('o') + '","ClosedAt":null,' +
  '"Changes":[{"Kind":"RegistryValue",' +
  '"Target":"HKCU\\Software\\ForgeConflict\\Campagne","ValueName":"Essai",' +
  '"PreviousValue":null,"NewValue":{"type":"i32","value":42},' +
  '"PreviousValueExisted":false,"AffectsDisplay":true}]}'

[System.IO.File]::WriteAllText((Join-Path $journal ($id.ToString('N') + '.json')), $tx, (New-Object System.Text.UTF8Encoding($false)))
Note "Transaction ouverte injectee dans le journal."

if (Test-Path $logDir) { Remove-Item $logDir -Recurse -Force -ErrorAction SilentlyContinue }
$p2 = Start-Process $exe -PassThru
Start-Sleep -Seconds 12
Stop-Process -Id $p2.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

$log2 = Join-Path $logDir 'forgeconflict.log'
if (Test-Path $log2) {
    $vu = @(Select-String -Path $log2 -Pattern 'interrompue')
    Note ("Transaction detectee au demarrage : " + ($vu.Count -gt 0))
    foreach ($l in $vu) { Note ("  " + $l.Line) }
}
Remove-Item $journal -Recurse -Force -ErrorAction SilentlyContinue

# --- 5. Garde-fou du mode sans echec --------------------------------
Titre "5. GARDE-FOU DU MODE SANS ECHEC"
Note "Test central : la machine doit ressortir seule du mode sans echec,"
Note "meme si rien d'autre ne s'execute."

$moi = $PSCommandPath
if (-not $moi) { $moi = 'C:\ForgeConflict\vm-campaign.ps1' }

$cmd2 = 'powershell -NoProfile -ExecutionPolicy Bypass -File "' + $moi + '" -SafeModePhase'
schtasks /create /tn "ForgeConflict-CampagnePhase2" /tr $cmd2 /sc onstart /ru SYSTEM /rl HIGHEST /f 2>&1 | Out-Null
Note "Phase 2 inscrite au demarrage."

# La commande passe par un fichier .bat : schtasks rejette une commande /tr
# contenant guillemets et esperluette, et la tache n'est alors PAS creee.
$batSortie = Join-Path $racine 'sortie-mode-sans-echec.bat'
$contenuBat = @(
    '@echo off',
    'bcdedit /deletevalue {current} safeboot',
    'schtasks /delete /tn "ForgeConflict-SortieModeSansEchec" /f',
    'exit /b 0'
) -join "`r`n"
[System.IO.File]::WriteAllText($batSortie, $contenuBat, [System.Text.Encoding]::ASCII)
Note ("Script de sortie ecrit : " + $batSortie)

$arm = (schtasks /create /tn "ForgeConflict-SortieModeSansEchec" /tr $batSortie /sc onstart /ru SYSTEM /rl HIGHEST /f 2>&1 | Out-String)

# La creation ne suffit pas : on relit la tache pour confirmer qu'elle existe.
# Armer sans verifier reviendrait a se fier a un filet jamais regarde.
$verif = (schtasks /query /tn "ForgeConflict-SortieModeSansEchec" 2>&1 | Out-String)
$arme = -not ($verif -match 'ERROR|ERREUR|introuvable|not exist')
Note ("Sortie de mode sans echec armee : " + $arme)

if (-not $arme) {
    Note "ARRET : sans garantie de sortie, l'entree en mode sans echec est refusee."
    Note "C'est le comportement voulu : mieux vaut ne rien faire que rester bloque."
    Note ("Detail : " + ($arm -replace "`r?`n", ' ').Trim())
    schtasks /delete /tn "ForgeConflict-CampagnePhase2" /f 2>&1 | Out-Null
    Note "CAMPAGNE TERMINEE (phase 5 non jouee)."
    return
}

bcdedit /set '{current}' safeboot minimal 2>&1 | Out-Null
Note "Indicateur de mode sans echec pose."
Note "PHASE 1 TERMINEE. Redemarrage dans 15 secondes."
Note "La machine passera en mode sans echec, la tache de sortie retirera"
Note "l'indicateur, et le redemarrage suivant la ramenera en mode normal."

shutdown /r /t 15 /c "ForgeConflict : test du garde-fou de mode sans echec"

}
catch {
    Note ("ERREUR INATTENDUE : " + $_.Exception.Message)
    Note ("  a la ligne " + $_.InvocationInfo.ScriptLineNumber)
}
