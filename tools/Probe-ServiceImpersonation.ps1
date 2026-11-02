<#
.SYNOPSIS
Probes whether the NL Query service account can read business data as another user via impersonation, read-only.
#>
param(
    [string]$BaseUrl = 'http://crm.example.com/contoso',
    [string]$ServiceAccount = 'CONTOSO\svc-nlquery',
    [string[]]$Impersonate = @('CONTOSO\Administrator', 'CONTOSO\testuser_a', 'CONTOSO\testuser_b'),
    [string]$Table = 'products'
)
$ErrorActionPreference = 'Stop'
$api = "$BaseUrl/api/data/v9.0"
$baseHeaders = @{ Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }
$cred = Get-Credential -UserName $ServiceAccount -Message 'NL Query SERVICE account (CRM read-only probe)'

<#
.SYNOPSIS
Sends a read-only GET to the CRM Web API, optionally as another caller.
#>
function Get-Crm([string]$Path, [string]$CallerId = '') {
    $h = $baseHeaders.Clone()
    if ($CallerId) { $h['MSCRMCallerID'] = $CallerId }
    Invoke-RestMethod -Uri "$api/$Path" -Method Get -Headers $h -Credential $cred
}
<#
.SYNOPSIS
Tries a read and prints whether it succeeded with a row count or failed with the error.
#>
function Try-Get([string]$Label, [string]$Path, [string]$CallerId = '') {
    try {
        $r = Get-Crm $Path $CallerId
        $count = @($r.value).Count
        Write-Host ("  OK       {0}: {1} row(s)" -f $Label, $count)
    }
    catch {
        $m = "$($_.ErrorDetails.Message)"
        if (-not $m) { $m = $_.Exception.Message }
        if ($m.Length -gt 330) { $m = $m.Substring(0, 330) }
        Write-Host ("  REFUSED  {0}: {1}" -f $Label, $m)
    }
}

Write-Host "Service account: $ServiceAccount"
$me = Get-Crm 'WhoAmI'
Write-Host ("WhoAmI (service): {0}" -f $me.UserId)

$ids = @{}
foreach ($a in $Impersonate) {
    $f = [uri]::EscapeDataString("domainname eq '$($a.Replace("'", "''"))'")
    $u = (Get-Crm ('systemusers?$select=systemuserid&$filter=' + $f)).value
    if (@($u).Count -eq 1) { $ids[$a] = $u[0].systemuserid } else { Write-Host "  (could not find user $a)" }
}

$path = $Table + '?$top=1'
Write-Host "`nSame read ($path) three ways:"
Try-Get '1. without MSCRMCallerID' $path
foreach ($a in $Impersonate) {
    if ($ids.ContainsKey($a)) { Try-Get "2. as $a" $path $ids[$a] }
}
Write-Host "`nImpersonated WhoAmI (which identity does CRM run as?):"
foreach ($a in $Impersonate) {
    if ($ids.ContainsKey($a)) {
        try { $w = Get-Crm 'WhoAmI' $ids[$a]; Write-Host ("  as {0}: UserId {1}" -f $a, $w.UserId) }
        catch { Write-Host ("  as {0}: {1}" -f $a, $_.Exception.Message) }
    }
}
