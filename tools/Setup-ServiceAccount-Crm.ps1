<#
.SYNOPSIS
Creates the minimal CRM security role for the NL Query service account and assigns it.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$BaseUrl = 'http://crm.example.com/contoso',
    [string]$Account = 'CONTOSO\svc-nlquery',
    [string]$RoleName = 'NL Query Service',
    [string[]]$ReadTables = @('account', 'contact', 'opportunity', 'product', 'contract', 'contractdetail', 'incident', 'salesorder'),
    [switch]$ReadAllTables,
    [string[]]$CreateTables = @(),
    [switch]$OpenInCrm,
    [string[]]$Privileges = @('prvActOnBehalfOfAnotherUser', 'prvReadUser', 'prvReadQuery', 'prvReadEntity', 'prvReadAttribute', 'prvReadRelationship', 'prvReadOptionSet')
)
$ErrorActionPreference = 'Stop'
$api = "$BaseUrl/api/data/v9.0"
$headers = @{ Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }

if ($env:Crm__Password) {
    $cred = New-Object Management.Automation.PSCredential('CONTOSO\Administrator', (ConvertTo-SecureString $env:Crm__Password -AsPlainText -Force))
}
else {
    $cred = Get-Credential -UserName 'CONTOSO\Administrator' -Message 'CRM Administrator (lab)'
}

<#
.SYNOPSIS
Sends a request to the CRM Web API as the administrator and returns the parsed response.
#>
function Invoke-Crm([string]$Method, [string]$Path, $Body = $null) {
    $args2 = @{ Uri = "$api/$Path"; Method = $Method; Headers = $headers; Credential = $cred; UseBasicParsing = $true }
    if ($null -ne $Body) { $args2['Body'] = ($Body | ConvertTo-Json -Depth 6); $args2['ContentType'] = 'application/json; charset=utf-8' }
    try { Invoke-WebRequest @args2 }
    catch {
        $msg = "$($_.ErrorDetails.Message)"
        if (-not $msg) { $msg = $_.Exception.Message }
        if ($msg.Length -gt 400) { $msg = $msg.Substring(0, 400) }
        throw "CRM $Method $Path failed: $msg"
    }
}
<#
.SYNOPSIS
Sends a GET to the CRM Web API as the administrator and returns the parsed response.
#>
function Get-Crm([string]$Path) { (Invoke-Crm 'GET' $Path).Content | ConvertFrom-Json }
<#
.SYNOPSIS
Escapes a value for safe use in a CRM Web API URL or filter.
#>
function Escape([string]$s) { [uri]::EscapeDataString($s.Replace("'", "''")) }

$root = (Get-Crm 'businessunits?$select=businessunitid,name&$filter=parentbusinessunitid eq null').value
if ($root.Count -ne 1) { throw "Expected one root business unit, found $($root.Count)." }
$buId = $root[0].businessunitid
"Root business unit: $($root[0].name)"

$role = (Get-Crm ('roles?$select=roleid,name&$filter=name eq ''' + (Escape $RoleName) + ''' and _businessunitid_value eq ' + $buId)).value
$roleId = $null
if ($role.Count -gt 0) { $roleId = $role[0].roleid; "Role '$RoleName' exists." }
elseif ($PSCmdlet.ShouldProcess("CRM", "Create role '$RoleName'")) {
    [void](Invoke-Crm 'POST' 'roles' @{ name = $RoleName; 'businessunitid@odata.bind' = "/businessunits($buId)" })
    $roleId = (Get-Crm ('roles?$select=roleid&$filter=name eq ''' + (Escape $RoleName) + ''' and _businessunitid_value eq ' + $buId)).value[0].roleid
    "Created role '$RoleName'."
}

if ($roleId) {
    $names = @($Privileges)
    if ($ReadAllTables) {
        $names += @((Get-Crm ('privileges?$select=name&$filter=startswith(name,''prvRead'')')).value | ForEach-Object { $_.name })
    }
    else {
        foreach ($table in $ReadTables) {
            $def = $null; $lastError = ''
            foreach ($select in @('SchemaName,Privileges', 'SchemaName')) {
                if ($def) { break }
                try { $def = Get-Crm ('EntityDefinitions(LogicalName=''' + $table + ''')?$select=' + $select) }
                catch { $lastError = $_.Exception.Message }
            }
            $read = @()
            if ($def -and $def.Privileges) {
                $read = @($def.Privileges | Where-Object { "$($_.PrivilegeType)" -match 'Read' } | ForEach-Object { $_.Name })
            }
            elseif ($def) {
                $guess = 'prvRead' + $def.SchemaName
                if (@((Get-Crm ('privileges?$select=name&$filter=name eq ''' + $guess + '''')).value).Count -gt 0) { $read = @($guess) }
            }
            else {
                $short = $lastError; if ($short.Length -gt 260) { $short = $short.Substring(0, 260) }
                Write-Warning "Could not read the definition of table '$table': $short"
                continue
            }
            if ($read.Count -gt 0) { $names += $read } else { Write-Host "Table '$table' has no read privilege of its own (it follows its parent table); skipped." }
        }
    }
    foreach ($table in $CreateTables) {
        $def = $null
        try { $def = Get-Crm ('EntityDefinitions(LogicalName=''' + $table + ''')?$select=SchemaName,Privileges') }
        catch { Write-Warning "Could not read the definition of table '$table': $($_.Exception.Message)"; continue }
        $create = @($def.Privileges | Where-Object { "$($_.PrivilegeType)" -match '^(Create|Append|AppendTo)$' } | ForEach-Object { $_.Name })
        if ($create.Count -gt 0) { $names += $create } else { Write-Host "Table '$table' has no create privilege of its own; skipped." }
    }
    if ($OpenInCrm) { $names += @('prvCreateUserQuery', 'prvReadUserQuery', 'prvWriteUserQuery', 'prvDeleteUserQuery') }
    $names = @($names | Select-Object -Unique)
    $toAdd = @()
    foreach ($name in $names) {
        $p = (Get-Crm ('privileges?$select=privilegeid,name,canbebasic,canbelocal,canbedeep,canbeglobal&$filter=name eq ''' + $name + '''')).value
        if ($p.Count -ne 1) {
            $hint = $name -replace '^prv(Read|Create|Write|Delete|Append|AppendTo|Assign|Share)', ''
            $near = (Get-Crm ('privileges?$select=name&$filter=contains(name,''' + $hint + ''')')).value | ForEach-Object { $_.name }
            throw "Privilege $name was not found in this CRM. Similar names: $($near -join ', ')"
        }
        $depth = if ($p[0].canbeglobal) { 'Global' } elseif ($p[0].canbedeep) { 'Deep' } elseif ($p[0].canbelocal) { 'Local' } else { 'Basic' }
        if ($depth -ne 'Global') { Write-Host "Privilege $name allows at most depth $depth in this CRM; granted at that depth." }
        $toAdd += @{ PrivilegeId = $p[0].privilegeid; Depth = $depth; BusinessUnitId = $buId }
    }
    if ($PSCmdlet.ShouldProcess("role '$RoleName'", "Set $($names.Count) privileges: $($names -join ', ')")) {
        [void](Invoke-Crm 'POST' "roles($roleId)/Microsoft.Dynamics.CRM.AddPrivilegesRole" @{ Privileges = $toAdd })
        "Role privileges set ($($names.Count)): $($names -join ', ')"
    }
}

$user = (Get-Crm ('systemusers?$select=systemuserid,fullname,accessmode,isdisabled&$filter=domainname eq ''' + (Escape $Account) + '''')).value
$userId = $null
if ($user.Count -gt 0) {
    $userId = $user[0].systemuserid
    "CRM user $Account exists (access mode $($user[0].accessmode); 4 = non-interactive, disabled = $($user[0].isdisabled))."
    if ($user[0].accessmode -ne 4) {
        if ($PSCmdlet.ShouldProcess("$Account", 'Set access mode to Non-interactive')) {
            try {
                [void](Invoke-Crm 'PATCH' "systemusers($userId)" @{ accessmode = 4; isintegrationuser = $true })
                "Set access mode of $Account to Non-interactive."
            }
            catch {
                Write-Warning "Could not switch the access mode: $($_.Exception.Message)"
                Write-Warning 'Do it in CRM: Settings > Security > Users > the user > Information form > Access Mode = Non-interactive.'
            }
        }
    }
}
elseif ($PSCmdlet.ShouldProcess("CRM", "Create non-interactive user $Account")) {
    $short = ($Account -split '\\')[1]
    [void](Invoke-Crm 'POST' 'systemusers' @{ domainname = $Account; firstname = 'NL Query'; lastname = 'Service'; accessmode = 4; isintegrationuser = $true; 'businessunitid@odata.bind' = "/businessunits($buId)" })
    $userId = (Get-Crm ('systemusers?$select=systemuserid&$filter=domainname eq ''' + (Escape $Account) + '''')).value[0].systemuserid
    "Created CRM user $Account (non-interactive)."
}

if ($userId -and $roleId) {
    $has = (Get-Crm "systemusers($userId)/systemuserroles_association?`$select=roleid").value | Where-Object { $_.roleid -eq $roleId }
    if ($has) { "The user already has the role." }
    elseif ($PSCmdlet.ShouldProcess("$Account", "Assign role '$RoleName'")) {
        [void](Invoke-Crm 'POST' "systemusers($userId)/systemuserroles_association/`$ref" @{ '@odata.id' = "$api/roles($roleId)" })
        "Assigned role '$RoleName' to $Account."
    }
}
""
"Next: .\Check-ServiceAccount.ps1 (read-only verification)."
