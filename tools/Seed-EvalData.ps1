<#
.SYNOPSIS
Adds fictional evaluation data to the lab CRM, idempotently, prompting for the password and never writing it to disk.
#>
param([string]$BaseUrl = 'http://crm.example.com/contoso')
$ErrorActionPreference = 'Stop'
$cred = Get-Credential -UserName 'CONTOSO\Administrator' -Message 'Lab CRM'
$api = "$BaseUrl/api/data/v9.0"
$headers = @{ Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }

<#
.SYNOPSIS
Converts an object to JSON encoded as UTF-8 bytes.
#>
function ConvertTo-Utf8Bytes($Object) {
    $bytes = [Text.Encoding]::UTF8.GetBytes(($Object | ConvertTo-Json -Depth 6))
    return , $bytes
}

<#
.SYNOPSIS
Sends a request to the CRM Web API with the given method, path and optional body.
#>
function Invoke-Crm([string]$Method, [string]$Path, $Body = $null) {
    $params = @{ Uri = "$api/$Path"; Method = $Method; Headers = $headers; Credential = $cred }
    if ($null -ne $Body) {
        $params.ContentType = 'application/json; charset=utf-8'
        $params.Body = ConvertTo-Utf8Bytes $Body
    }
    try { Invoke-RestMethod @params }
    catch {
        $detail = $_.ErrorDetails.Message
        throw "CRM $Method $Path failed: $($_.Exception.Message) $detail"
    }
}

<#
.SYNOPSIS
Finds a record id by its name in a CRM entity set, or returns nothing.
#>
function Find-Id([string]$Set, [string]$IdField, [string]$NameField, [string]$Name) {
    $escaped = $Name.Replace("'", "''")
    $filter = [uri]::EscapeDataString("$NameField eq '$escaped'")
    $r = Invoke-Crm 'GET' "$Set`?`$select=$IdField&`$filter=$filter&`$top=1"
    if ($r.value.Count -gt 0) { return $r.value[0].$IdField } else { return $null }
}

<#
.SYNOPSIS
Returns the id of the named record, creating it first if it does not exist.
#>
function Ensure-Record([string]$Set, [string]$IdField, [string]$NameField, [string]$Name, [hashtable]$Body) {
    $id = Find-Id $Set $IdField $NameField $Name
    if ($id) { Write-Host "exists:  $Name"; return $id }
    $Body[$NameField] = $Name
    $createHeaders = $headers.Clone()
    $createHeaders['Prefer'] = 'return=representation'
    try {
        $created = Invoke-RestMethod -Uri "$api/$Set" -Method Post -Headers $createHeaders -Credential $cred `
            -ContentType 'application/json; charset=utf-8' -Body (ConvertTo-Utf8Bytes $Body)
    }
    catch {
        throw "CRM POST $Set ($Name) failed: $($_.Exception.Message) $($_.ErrorDetails.Message)"
    }
    Write-Host "created: $Name"
    return $created.$IdField
}

$accounts = @{}
foreach ($n in @('Örnekova Holding A.Ş.', 'Denema Sigorta A.Ş.', 'Denema Sağlık Sigorta', 'ZKV Örnek Sistemleri A.Ş.', 'XSS <b>Probe</b> Ltd', 'Hamburg Logistik GmbH')) {
    $accounts[$n] = Ensure-Record 'accounts' 'accountid' 'name' $n @{}
}

$templates = (Invoke-Crm 'GET' 'contracttemplates?$select=contracttemplateid&$top=1').value
if ($templates.Count -eq 0) {
    $templateId = Ensure-Record 'contracttemplates' 'contracttemplateid' 'name' 'Eval Template' @{ abbreviation = 'EVT'; billingfrequencycode = 2; allotmenttypecode = 1 }
} else { $templateId = $templates[0].contracttemplateid }
$product = (Invoke-Crm 'GET' 'products?$select=productid,_defaultuomid_value&$top=1').value[0]

$plan = @(
    @('Örnekova Holding A.Ş.', 'Örnekova Support Contract', @('Örnekova Support Line A', 'Örnekova Support Line B')),
    @('Denema Sigorta A.Ş.', 'Denema Annual Contract', @('Denema Line A')),
    @('Denema Sağlık Sigorta', 'Denema Sağlık Contract', @('Denema Sağlık Line A')),
    @('ZKV Örnek Sistemleri A.Ş.', 'ZKV Annual Contract', @('ZKV Line A'))
)
foreach ($p in $plan) {
    $accId = $accounts[$p[0]]
    $contractId = Ensure-Record 'contracts' 'contractid' 'title' $p[1] @{
        'customerid_account@odata.bind'        = "/accounts($accId)"
        'billingcustomerid_account@odata.bind' = "/accounts($accId)"
        'contracttemplateid@odata.bind'        = "/contracttemplates($templateId)"
        activeon = '2026-01-01T00:00:00Z'
        expireson = '2027-01-01T00:00:00Z'
    }
    foreach ($lineTitle in $p[2]) {
        Ensure-Record 'contractdetails' 'contractdetailid' 'title' $lineTitle @{
            'contractid@odata.bind' = "/contracts($contractId)"
            'productid@odata.bind'  = "/products($($product.productid))"
            'uomid@odata.bind'      = "/uoms($($product._defaultuomid_value))"
            activeon = '2026-01-01T00:00:00Z'
            expireson = '2027-01-01T00:00:00Z'
            price = 100
        } | Out-Null
    }
}
Write-Host "Done."
