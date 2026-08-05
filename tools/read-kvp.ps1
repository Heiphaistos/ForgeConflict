#Requires -Version 5.1
# Lit les valeurs renvoyees par l'invite via le canal KVP d'Hyper-V.
param([string]$VMName = 'W11 dev')

$vm = Get-CimInstance -Namespace root\virtualization\v2 -ClassName Msvm_ComputerSystem -Filter "ElementName='$VMName'"
$kvp = $vm | Get-CimAssociatedInstance -ResultClassName Msvm_KvpExchangeComponent

$items = $kvp.GuestExchangeItems
if (-not $items) {
    Write-Host "Aucune donnee renvoyee par l'invite pour l'instant."
    return
}

$found = $false
foreach ($x in $items) {
    $props = ([xml]$x).INSTANCE.PROPERTY
    $name = ($props | Where-Object Name -eq 'Name').VALUE
    $data = ($props | Where-Object Name -eq 'Data').VALUE
    if ($name -like 'FC_*') {
        $found = $true
        Write-Host ""
        Write-Host ("=== " + $name + " ===") -ForegroundColor Cyan
        Write-Host $data
    }
}
if (-not $found) { Write-Host "Aucune cle FC_* : la verification n'a pas encore tourne." }
