<#
.SYNOPSIS
Performs a read-only check of the NL Query service account in CRM and writes nothing.
#>
param(
    [string]$BaseUrl = 'http://crm.example.com/contoso',
    [string]$Account = 'CONTOSO\svc-nlquery',
    [switch]$AllowNormalAccessMode,
    [string[]]$CreateTables = @(),
    [switch]$OpenInCrm
)
$ErrorActionPreference = 'Stop'
$api = "$BaseUrl/api/data/v9.0"
$headers = @{ Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }

if ($env:Crm__Password) {
    $cred = New-Object Management.Automation.PSCredential('CONTOSO\Administrator', (ConvertTo-SecureString $env:Crm__Password -AsPlainText -Force))
}
else {
    $cred = Get-Credential -UserName 'CONTOSO\Administrator' -Message 'CRM Administrator (lab, read only)'
}

<#
.SYNOPSIS
Reads from the CRM Web API, optionally impersonating a caller id.
#>
function Get-Crm([string]$Path, [string]$CallerId = '') {
    $h = $headers.Clone()
    if ($CallerId) { $h['MSCRMCallerID'] = $CallerId }
    Invoke-RestMethod -Uri "$api/$Path" -Method Get -Headers $h -Credential $cred
}
<#
.SYNOPSIS
Escapes a string for use inside an OData query value.
#>
function Escape([string]$s) { [uri]::EscapeDataString($s.Replace("'", "''")) }

$script:failed = 0
<#
.SYNOPSIS
Prints an OK or FAIL line for a check result.
#>
function Report([bool]$Ok, [string]$Text) {
    if ($Ok) { Write-Host "  PASS  $Text" } else { Write-Host "  FAIL  $Text"; $script:failed++ }
}
<#
.SYNOPSIS
Runs a read as the service user and returns null on success or the shortened error text.
#>
function Try-Read([string]$Path, [string]$CallerId) {
    try { [void](Get-Crm $Path $CallerId); return $null }
    catch {
        $m = "$($_.ErrorDetails.Message)"
        if (-not $m) { $m = $_.Exception.Message }
        if ($m.Length -gt 160) { $m = $m.Substring(0, 160) }
        return $m
    }
}

Write-Host "1. User $Account"
$u = (Get-Crm ('systemusers?$select=systemuserid,accessmode,isdisabled&$filter=domainname eq ''' + (Escape $Account) + '''')).value
Report ($u.Count -eq 1) 'the CRM user exists'
if ($u.Count -ne 1) { Write-Host 'Stopping: create it with Setup-ServiceAccount-Crm.ps1.'; exit 1 }
$id = $u[0].systemuserid
if ($u[0].accessmode -ne 4 -and $AllowNormalAccessMode) { Write-Host "  WARN  access mode is $($u[0].accessmode), not Non-interactive (4): accepted for the lab only (-AllowNormalAccessMode)" }
else { Report ($u[0].accessmode -eq 4) "access mode is Non-interactive (value $($u[0].accessmode), expected 4)" }
Report (-not $u[0].isdisabled) 'the user is enabled'

Write-Host '2. Roles and rights'
$roleIds = @((Get-Crm "systemusers($id)/systemuserroles_association?`$select=roleid,name").value)
$names = @($roleIds | ForEach-Object { $_.name })
Write-Host "      roles: $($names -join ', ')"
Report ($roleIds.Count -ge 1) 'the user has a role'
Report (-not ($names -contains 'System Administrator' -or $names -contains 'System Customizer')) 'no administrator-type role'
$act = (Get-Crm 'privileges?$select=privilegeid&$filter=name eq ''prvActOnBehalfOfAnotherUser''').value
$mine = @((Get-Crm "systemusers($id)/Microsoft.Dynamics.CRM.RetrieveUserPrivileges()").RolePrivileges | ForEach-Object { $_.PrivilegeId })
Report ($act.Count -eq 1 -and ($mine -contains $act[0].privilegeid)) 'holds the right to act on behalf of another user'

Write-Host '3. The service reads, replayed as this account (MSCRMCallerID)'
$checks = @(
    @{ Text = 'WhoAmI (health check)';                Path = 'WhoAmI' },
    @{ Text = 'entity list (metadata)';               Path = 'EntityDefinitions?$select=LogicalName,EntitySetName,DisplayName,IsPrivate' },
    @{ Text = 'entity with fields and relationships'; Path = 'EntityDefinitions(LogicalName=''account'')?$select=LogicalName,EntitySetName&$expand=Attributes($select=LogicalName,AttributeType),ManyToOneRelationships($select=ReferencingAttribute),OneToManyRelationships($select=ReferencingAttribute)' },
    @{ Text = 'option sets of choice fields';         Path = 'EntityDefinitions(LogicalName=''account'')/Attributes/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?$select=LogicalName&$expand=OptionSet($select=Options)' },
    @{ Text = 'object type code (Open in CRM)';       Path = 'EntityDefinitions(LogicalName=''account'')?$select=ObjectTypeCode' },
    @{ Text = 'user lookup by Windows account';       Path = 'systemusers?$select=systemuserid,domainname&$top=1' },
    @{ Text = 'default view columns (savedquery)';    Path = 'savedqueries?$select=savedqueryid&$top=1' }
)
foreach ($c in $checks) {
    $err = Try-Read $c.Path $id
    Report ($null -eq $err) ($c.Text + $(if ($err) { " -> $err" } else { '' }))
}
Write-Host '4. Read only: the role holds no write-type privilege'
$all = @()
foreach ($r in $roleIds) { $all += @((Get-Crm "roles($($r.roleid))/roleprivileges_association?`$select=name").value | ForEach-Object { $_.name }) }
$all = @($all | Select-Object -Unique)
$knownDependencies = @('prvCreateSharePointData', 'prvWriteSharePointData')
$writeLike = @($all | Where-Object { $_ -match '^prv(Create|Write|Delete|Assign|Share|Append|AppendTo)' })
$allowedCreate = @()
foreach ($table in $CreateTables) {
    $def = Get-Crm ('EntityDefinitions(LogicalName=''' + $table + ''')?$select=SchemaName,Privileges')
    $allowedCreate += @($def.Privileges | Where-Object { "$($_.PrivilegeType)" -match '^(Create|Append|AppendTo)$' } | ForEach-Object { $_.Name })
}
if ($OpenInCrm) { $allowedCreate += @('prvCreateUserQuery', 'prvWriteUserQuery', 'prvDeleteUserQuery') }
foreach ($d in @($writeLike | Where-Object { $allowedCreate -contains $_ })) { Write-Host "  NOTE  $d is granted on purpose for record creation (-CreateTables)" }
$writes = @($writeLike | Where-Object { ($knownDependencies -notcontains $_) -and ($allowedCreate -notcontains $_) })
foreach ($d in @($writeLike | Where-Object { $knownDependencies -contains $_ })) { Write-Host "  NOTE  $d is a CRM-added dependency (harmless: the service performs no writes)" }
Write-Host ("      privileges in the role(s): {0} ({1} read)" -f $all.Count, @($all | Where-Object { $_ -match '^prvRead' }).Count)
Report ($writes.Count -eq 0) ('no create/write/delete/assign/share/append privilege' + $(if ($writes.Count -gt 0) { ': ' + ($writes -join ', ') } else { '' }))

if ($script:failed -gt 0) { Write-Host "`n$($script:failed) check(s) FAILED"; exit 1 }
Write-Host "`nAll checks passed"
exit 0
