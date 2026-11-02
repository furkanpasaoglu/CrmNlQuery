<#
.SYNOPSIS
Dumps real lab CRM data to the console for writing eval cases, read-only.
#>
param([string]$BaseUrl = 'http://crm.example.com/contoso')
$ErrorActionPreference = 'Stop'
$cred = Get-Credential -UserName 'CONTOSO\Administrator' -Message 'Lab CRM (read only)'
$api = "$BaseUrl/api/data/v9.0"
$headers = @{ Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }

<#
.SYNOPSIS
Performs a GET request against the lab CRM.
#>
function Get-Crm([string]$Path) {
    try { Invoke-RestMethod -Uri "$api/$Path" -Method Get -Headers $headers -Credential $cred }
    catch { throw "CRM GET $Path failed: $($_.Exception.Message) $($_.ErrorDetails.Message)" }
}

Write-Host "=== accounts (name | city | country), first 100 by name ==="
$accounts = (Get-Crm 'accounts?$select=name,address1_city,address1_country&$orderby=name asc&$top=100').value
foreach ($a in $accounts) { Write-Host ("{0} | {1} | {2}" -f $a.name, $a.address1_city, $a.address1_country) }
Write-Host ("total shown: {0}" -f $accounts.Count)

Write-Host ""
Write-Host "=== accounts per city ==="
$accounts | Where-Object { $_.address1_city } | Group-Object address1_city | Sort-Object Name |
    ForEach-Object { Write-Host ("{0}: {1}" -f $_.Name, $_.Count) }

Write-Host ""
Write-Host "=== account names that contain a city name of another account ==="
$cities = $accounts | Where-Object { $_.address1_city } | ForEach-Object { $_.address1_city } | Sort-Object -Unique
foreach ($a in $accounts) {
    foreach ($c in $cities) {
        if ($a.name -like "*$c*") { Write-Host ("{0}  (contains '{1}', city of this account: {2})" -f $a.name, $c, $a.address1_city) }
    }
}

Write-Host ""
Write-Host "=== contracts per account (read only) ==="
$names = @{}
foreach ($a in (Get-Crm 'accounts?$select=accountid,name&$top=100').value) { $names[$a.accountid] = $a.name }
$contracts = (Get-Crm 'contracts?$select=title,_customerid_value&$top=200').value
foreach ($n in ($names.Values | Sort-Object)) {
    $count = @($contracts | Where-Object { $names[$_._customerid_value] -eq $n }).Count
    if ($count -gt 0) { Write-Host ("{0}: {1} contract(s)" -f $n, $count) }
}
Write-Host ("contracts total: {0}; Hamburg Logistik GmbH: {1}" -f $contracts.Count, @($contracts | Where-Object { $names[$_._customerid_value] -eq 'Hamburg Logistik GmbH' }).Count)