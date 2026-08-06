#Requires -Version 5.1
# Verifie que l'application detecte au demarrage une transaction laissee ouverte
# par une interruption. La transaction est fabriquee a la main : aucune ecriture
# systeme n'a lieu, on teste le mecanisme de reprise, pas une remediation.
$ErrorActionPreference = 'Stop'

$journal = Join-Path $env:ProgramData 'ForgeConflict\journal'
$logDir = 'D:\Projet\ForgeConflict\publish\.logs'
$exe = 'D:\Projet\ForgeConflict\publish\ForgeConflict.exe'

if (Test-Path $journal) { Remove-Item $journal -Recurse -Force }
if (Test-Path $logDir) { Remove-Item $logDir -Recurse -Force }

function Lancer-Et-Fermer {
    $p = Start-Process $exe -PassThru
    Start-Sleep -Seconds 7
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

Write-Host "=== 1. Demarrage sur journal vierge ==="
Lancer-Et-Fermer
$log = Join-Path $logDir 'forgeconflict.log'
$avert = Select-String -Path $log -Pattern 'transaction' -ErrorAction SilentlyContinue
Write-Host ("  mentions de transaction : " + $(if ($avert) { $avert.Count } else { 0 }) + "  (attendu 0)")

Write-Host ""
Write-Host "=== 2. Injection d'une transaction interrompue ==="
New-Item -ItemType Directory -Path $journal -Force | Out-Null
$id = [Guid]::NewGuid()
$json = @"
{
  "Id": "$id",
  "RuleId": "FG-TDR-001",
  "Title": "Allonger le delai TDR",
  "Risk": "Eleve",
  "State": "Ouverte",
  "OpenedAt": "$((Get-Date).ToString('o'))",
  "ClosedAt": null,
  "Changes": [
    {
      "Kind": "RegistryValue",
      "Target": "HKLM\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers",
      "ValueName": "TdrDelay",
      "PreviousValue": null,
      "NewValue": 8,
      "PreviousValueExisted": false,
      "AffectsDisplay": true
    }
  ]
}
"@
$file = Join-Path $journal ($id.ToString('N') + '.json')
[System.IO.File]::WriteAllText($file, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "  ecrite : $file"

Remove-Item $logDir -Recurse -Force -ErrorAction SilentlyContinue
Lancer-Et-Fermer

Write-Host ""
Write-Host "=== 3. L'application l'a-t-elle vue ? ==="
if (Test-Path $log) {
    $lignes = Select-String -Path $log -Pattern 'transaction|interrompue'
    if ($lignes) { $lignes | ForEach-Object { Write-Host ("  " + $_.Line) } }
    else { Write-Host "  ECHEC : aucune mention dans le journal" }

    $err = Select-String -Path $log -Pattern '\[ERROR\]'
    Write-Host ("  exceptions : " + $(if ($err) { $err.Count } else { 0 }))
} else {
    Write-Host "  ECHEC : journal absent"
}

Write-Host ""
Write-Host "=== 4. Journal transactionnel intact ? ==="
Write-Host ("  transactions sur disque : " + (Get-ChildItem $journal -Filter '*.json').Count)

Remove-Item $journal -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "  nettoye"
