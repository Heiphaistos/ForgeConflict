#Requires -Version 5.1
# Verification portable de ForgeConflict dans une VM sans SDK .NET.
# Les resultats sont renvoyes a l'hote par le canal KVP d'Hyper-V.
# ASCII uniquement : PowerShell 5.1 corrompt l'UTF-8 dans ce contexte.

$ErrorActionPreference = 'Continue'
$exe = 'C:\ForgeConflict\ForgeConflict.exe'
$report = 'C:\ForgeConflict\rapport.txt'
$kvpKey = 'HKLM:\SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters'
$lines = New-Object System.Collections.Generic.List[string]

function Note([string]$m) {
    $lines.Add($m)
    Write-Host $m
}

function Send-ToHost([string]$name, [string]$value) {
    try {
        # -Force sur New-ItemProperty ne force que la VALEUR. Sur une installation
        # Windows neuve la cle Guest\Parameters n'existe pas encore et toutes les
        # ecritures echouent : il faut creer la cle explicitement.
        if (-not (Test-Path $kvpKey)) {
            New-Item -Path $kvpKey -Force -ErrorAction Stop | Out-Null
            Note "KVP        : cle Guest\Parameters creee"
        }
        if ($value.Length -gt 900) { $value = $value.Substring(0, 900) }
        New-ItemProperty -Path $kvpKey -Name $name -Value $value -PropertyType String -Force -ErrorAction Stop | Out-Null
    } catch {
        # Trace dans le rapport, pas seulement a l'ecran : sinon un echec du canal
        # reste totalement invisible depuis l'hote.
        Note ("KVP " + $name + " : ECHEC -> " + $_.Exception.Message)
    }
}

Note "=== Verification ForgeConflict ==="
Note ("Machine    : " + $env:COMPUTERNAME)
Note ("OS         : " + (Get-CimInstance Win32_OperatingSystem).Caption + " build " + [Environment]::OSVersion.Version.Build)

# Un SDK .NET installe invaliderait la preuve de portabilite.
$sdk = $null
try { $sdk = (Get-Command dotnet -ErrorAction SilentlyContinue).Source } catch { }
$sdkState = if ($sdk) { "PRESENT ($sdk) - la preuve de portabilite est affaiblie" } else { "absent - preuve valide" }
Note ("SDK .NET   : " + $sdkState)
Send-ToHost 'FC_Sdk' $sdkState

$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Note ("Script elev: " + $elevated)

if (-not (Test-Path $exe)) {
    Note "ECHEC : executable introuvable"
    Send-ToHost 'FC_Result' 'ECHEC - exe introuvable'
    return
}

# Purger un journal d'une execution precedente pour ne pas lire d'ancien resultat.
$logDir = 'C:\ForgeConflict\.logs'
if (Test-Path $logDir) { Remove-Item $logDir -Recurse -Force -ErrorAction SilentlyContinue }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$proc = Start-Process $exe -PassThru
Note ("PID        : " + $proc.Id)

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

$win = $null
for ($i = 0; $i -lt 50 -and -not $win; $i++) {
    Start-Sleep -Milliseconds 400
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
    $win = $AE::RootElement.FindFirst($TS::Children, $cond)
}

if (-not $win) {
    Note "ECHEC : la fenetre ne s'est pas ouverte (runtime .NET manquant ?)"
    Send-ToHost 'FC_Result' 'ECHEC - fenetre absente'
    $lines -join "`r`n" | Set-Content $report -Encoding UTF8
    return
}
Note ("Fenetre    : '" + $win.Current.Name + "'")
Send-ToHost 'FC_Window' $win.Current.Name

function Get-Child([string]$id) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    return $win.FindFirst($TS::Descendants, $c)
}

# --- Auto-test du socle ---
$btn = Get-Child 'SelfTest'
if ($btn) {
    $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 3
    $out = Get-Child 'Output'
    $text = $out.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    Note "--- sortie de l'auto-test ---"
    Note $text
    Send-ToHost 'FC_SelfTest' ($text -replace "`r`n", ' | ')
} else {
    Note "ECHEC : bouton d'auto-test introuvable"
    Send-ToHost 'FC_Result' 'ECHEC - bouton absent'
}

# --- Analyse de l'inventaire ---
# La VM n'expose qu'un adaptateur Microsoft Hyper-V Video : c'est le cas limite
# qui doit etre traite proprement plutot que faire echouer la collecte.
$scan = Get-Child 'Scan'
if ($scan) {
    $scan.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 12
    $out = Get-Child 'Output'
    $report = $out.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    Note "--- rapport d'analyse ---"
    Note $report

    $complete = $report -match 'Toutes les sources ont repondu'
    Note ("Toutes les sources ont repondu : " + $complete)
    Send-ToHost 'FC_Scan' ($report -replace "`r`n", ' | ')
    Send-ToHost 'FC_ScanComplete' ("complet=$complete")
} else {
    Note "bouton d'analyse introuvable (version anterieure a 0.2.0 ?)"
}

# --- Resistance a une exception non geree ---
$provoke = Get-Child 'Provoke'
if ($provoke) {
    $provoke.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    $stillThere = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
    $alive = ($null -ne $stillThere) -and (-not $stillThere.HasExited)
    $drivable = $null -ne (Get-Child 'Output')
    Note ("Vivant apres exception     : " + $alive)
    Note ("Interface encore pilotable : " + $drivable)
    Send-ToHost 'FC_Alive' ("vivant=$alive pilotable=$drivable")
}

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# --- Journal produit ---
$logFile = Join-Path $logDir 'forgeconflict.log'
if (Test-Path $logFile) {
    $log = Get-Content $logFile -Encoding UTF8
    Note ("Journal    : " + $log.Count + " lignes")
    $levels = ($log | Select-String -Pattern '\[(INFO|WARN|ERROR)\]' -AllMatches | ForEach-Object { $_.Matches.Value }) -join ' '
    Send-ToHost 'FC_Log' ("lignes=" + $log.Count + " niveaux=" + $levels)
} else {
    Note "Journal    : AUCUN"
    Send-ToHost 'FC_Log' 'aucun journal'
}

Send-ToHost 'FC_Result' ('TERMINE ' + (Get-Date -Format 'o'))
Note ("Rapport ecrit dans " + $report)
# Ecriture en dernier : toute note ajoutee apres ce point serait perdue.
$lines -join "`r`n" | Set-Content $report -Encoding UTF8
