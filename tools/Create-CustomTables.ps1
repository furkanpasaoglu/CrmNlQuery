<#
.SYNOPSIS
Creates the custom tables of the custom-table baseline test in the lab CRM and loads fictional data into them.
#>
param(
    [string]$BaseUrl = 'http://crm.example.com/contoso',
    [string]$Account = 'CONTOSO\Administrator',
    [string]$SchemaFile = (Join-Path $PSScriptRoot 'custom-schema.json'),
    [string]$DataFile = (Join-Path $PSScriptRoot 'custom-data.json'),
    [switch]$Apply,
    [switch]$SkipData,
    [System.Management.Automation.PSCredential]$Credential
)
$ErrorActionPreference = 'Stop'
if ($env:NLQUERY_LAB_CRM -ne '1') { throw 'This script writes test data or runs evaluations against a CRM. Set $env:NLQUERY_LAB_CRM = ''1'' only when the CRM is a lab or test system.' }
$api = "$BaseUrl/api/data/v9.0"
$headers = @{ Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }
$cred = if ($Credential) { $Credential } else { Get-Credential -UserName $Account -Message 'Lab CRM account (writes only with -Apply)' }

$schema = Get-Content -Raw -Encoding UTF8 $SchemaFile | ConvertFrom-Json
$data = Get-Content -Raw -Encoding UTF8 $DataFile | ConvertFrom-Json

<#
.SYNOPSIS
Sends a request to the CRM Web API and returns the response.
#>
function Invoke-Crm([string]$Method, [string]$Path, $Body = $null, [hashtable]$ExtraHeaders = $null) {
    $h = $headers.Clone()
    if ($ExtraHeaders) { foreach ($k in $ExtraHeaders.Keys) { $h[$k] = $ExtraHeaders[$k] } }
    $params = @{ Uri = "$api/$Path"; Method = $Method; Headers = $h; Credential = $cred }
    if ($null -ne $Body) {
        $params.ContentType = 'application/json; charset=utf-8'
        $params.Body = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 14))
    }
    try { Invoke-RestMethod @params }
    catch {
        $m = "$($_.ErrorDetails.Message)"; if (-not $m) { $m = $_.Exception.Message }
        if ($m.Length -gt 400) { $m = $m.Substring(0, 400) }
        throw "CRM $Method $Path failed: $m"
    }
}

<#
.SYNOPSIS
Builds a localized label object for CRM metadata.
#>
function Label([string]$text) {
    [ordered]@{
        '@odata.type' = 'Microsoft.Dynamics.CRM.Label'
        LocalizedLabels = @([ordered]@{ '@odata.type' = 'Microsoft.Dynamics.CRM.LocalizedLabel'; Label = $text; LanguageCode = 1033 })
    }
}
<#
.SYNOPSIS
Builds the required-level object for CRM attribute metadata.
#>
function Required { [ordered]@{ Value = 'None'; CanBeChanged = $true; ManagedPropertyLogicalName = 'canmodifyrequirementlevelsettings' } }

<#
.SYNOPSIS
Builds an option set definition whose values start at 100000000 from a list of labels.
#>
function Option-Set($list, [string]$kind) {
    $options = @()
    for ($i = 0; $i -lt $list.Count; $i++) { $options += [ordered]@{ Value = (100000000 + $i); Label = (Label $list[$i]) } }
    [ordered]@{ '@odata.type' = 'Microsoft.Dynamics.CRM.OptionSetMetadata'; IsGlobal = $false; OptionSetType = $kind; Options = $options }
}

<#
.SYNOPSIS
Merges dictionaries into one ordered dictionary.
#>
function Join-Body($head, $common, $extra) {
    $out = [ordered]@{}
    foreach ($d in @($head, $common, $extra)) { foreach ($k in $d.Keys) { $out[$k] = $d[$k] } }
    return $out
}

<#
.SYNOPSIS
Builds the metadata body of one column from its definition.
#>
function New-AttributeBody($a, $optionLists) {
    $common = [ordered]@{ SchemaName = $a.schema; DisplayName = (Label $a.display); Description = (Label $a.display); RequiredLevel = (Required) }
    switch ($a.type) {
        'picklist' {
            return (Join-Body ([ordered]@{ '@odata.type' = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'; AttributeType = 'Picklist'; AttributeTypeName = @{ Value = 'PicklistType' } }) $common @{ OptionSet = (Option-Set @($optionLists.($a.optionsFrom)) 'Picklist') })
        }
        'multiselect' {
            return (Join-Body ([ordered]@{ '@odata.type' = 'Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata'; AttributeType = 'Virtual'; AttributeTypeName = @{ Value = 'MultiSelectPicklistType' } }) $common @{ OptionSet = (Option-Set @($optionLists.($a.optionsFrom)) 'Picklist') })
        }
        'boolean' {
            $os = [ordered]@{
                '@odata.type' = 'Microsoft.Dynamics.CRM.BooleanOptionSetMetadata'; OptionSetType = 'Boolean'; IsGlobal = $false
                TrueOption = [ordered]@{ Value = 1; Label = (Label $a.trueLabel) }; FalseOption = [ordered]@{ Value = 0; Label = (Label $a.falseLabel) }
            }
            return (Join-Body ([ordered]@{ '@odata.type' = 'Microsoft.Dynamics.CRM.BooleanAttributeMetadata'; AttributeType = 'Boolean'; AttributeTypeName = @{ Value = 'BooleanType' } }) $common @{ OptionSet = $os; DefaultValue = $false })
        }
        'integer' {
            return (Join-Body ([ordered]@{ '@odata.type' = 'Microsoft.Dynamics.CRM.IntegerAttributeMetadata'; AttributeType = 'Integer'; AttributeTypeName = @{ Value = 'IntegerType' } }) $common @{ Format = 'None'; MinValue = $a.min; MaxValue = $a.max })
        }
        'decimal' {
            return (Join-Body ([ordered]@{ '@odata.type' = 'Microsoft.Dynamics.CRM.DecimalAttributeMetadata'; AttributeType = 'Decimal'; AttributeTypeName = @{ Value = 'DecimalType' } }) $common @{ Precision = $a.precision; MinValue = 0; MaxValue = 100000000 })
        }
        'dateonly' {
            return (Join-Body ([ordered]@{ '@odata.type' = 'Microsoft.Dynamics.CRM.DateTimeAttributeMetadata'; AttributeType = 'DateTime'; AttributeTypeName = @{ Value = 'DateTimeType' } }) $common @{ Format = 'DateOnly'; DateTimeBehavior = @{ Value = 'DateOnly' } })
        }
        'memo' {
            return (Join-Body ([ordered]@{ '@odata.type' = 'Microsoft.Dynamics.CRM.MemoAttributeMetadata'; AttributeType = 'Memo'; AttributeTypeName = @{ Value = 'MemoType' } }) $common @{ Format = 'TextArea'; MaxLength = 2000 })
        }
        default { throw "Unknown column type '$($a.type)' for $($a.schema)." }
    }
}

$optionLists = $data.options
$created = @()
$problems = @()
<#
.SYNOPSIS
Writes a status note to the console.
#>
function Note([string]$line) { Write-Host $line }

<#
.SYNOPSIS
Checks whether a CRM entity already exists.
#>
function Entity-Exists([string]$logical) {
    $r = Invoke-Crm 'GET' "EntityDefinitions?`$select=LogicalName&`$filter=LogicalName eq '$logical'"
    return (@($r.value).Count -gt 0)
}
<#
.SYNOPSIS
Checks whether a CRM attribute already exists on an entity.
#>
function Attribute-Exists([string]$entity, [string]$logical) {
    $r = Invoke-Crm 'GET' "EntityDefinitions(LogicalName='$entity')/Attributes?`$select=LogicalName&`$filter=LogicalName eq '$logical'"
    return (@($r.value).Count -gt 0)
}
<#
.SYNOPSIS
Checks whether a CRM relationship already exists.
#>
function Relationship-Exists([string]$name) {
    $r = Invoke-Crm 'GET' "RelationshipDefinitions?`$select=SchemaName&`$filter=SchemaName eq '$name'"
    return (@($r.value).Count -gt 0)
}

Note "`n=== 1) Tables and columns ==="
foreach ($e in $schema.entities) {
    $exists = Entity-Exists $e.schema
    if (-not $exists) {
        Note ("  table {0}: {1}" -f $e.schema, $(if ($Apply) { 'creating...' } else { 'MISSING (would create)' }))
        if ($Apply) {
            $primary = [ordered]@{
                '@odata.type' = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'; AttributeType = 'String'; AttributeTypeName = @{ Value = 'StringType' }
                SchemaName = $e.primary.schema; IsPrimaryName = $true; RequiredLevel = (Required); MaxLength = 200; FormatName = @{ Value = 'Text' }
                DisplayName = (Label $e.primary.display); Description = (Label $e.primary.display)
            }
            $body = [ordered]@{
                '@odata.type' = 'Microsoft.Dynamics.CRM.EntityMetadata'; SchemaName = $e.schema
                DisplayName = (Label $e.display); DisplayCollectionName = (Label $e.plural); Description = (Label $e.description)
                OwnershipType = 'UserOwned'; IsActivity = $false; HasActivities = $false; HasNotes = $false; Attributes = @($primary)
            }
            [void](Invoke-Crm 'POST' 'EntityDefinitions' $body)
            $created += $e.schema
        }
    }
    else { Note ("  table {0}: exists" -f $e.schema) }
    foreach ($a in $e.attributes) {
        if ($exists -or $Apply) {
            $has = if ($exists) { Attribute-Exists $e.schema $a.schema } else { $false }
            if ($has) { continue }
        }
        Note ("    column {0}.{1} ({2}): {3}" -f $e.schema, $a.schema, $a.type, $(if ($Apply) { 'creating...' } else { 'MISSING (would create)' }))
        if ($Apply) {
            try {
                [void](Invoke-Crm 'POST' "EntityDefinitions(LogicalName='$($e.schema)')/Attributes" (New-AttributeBody $a $optionLists))
                $created += "$($e.schema).$($a.schema)"
            }
            catch {
                if ($a.optional) { Note "      SKIPPED (optional column, probably not supported by this CRM build): $($_.Exception.Message)"; $problems += "optional column $($a.schema) skipped" }
                else { throw }
            }
        }
    }
}

Note "`n=== 2) Relations ==="
foreach ($l in $schema.lookups) {
    $has = Relationship-Exists $l.relationship
    if ($has) { Note ("  {0}: exists" -f $l.relationship); continue }
    Note ("  {0} ({1} -> {2}): {3}" -f $l.relationship, $l.referencing, $l.referenced, $(if ($Apply) { 'creating...' } else { 'MISSING (would create)' }))
    if ($Apply) {
        $body = [ordered]@{
            '@odata.type' = 'Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata'; SchemaName = $l.relationship
            ReferencedEntity = $l.referenced; ReferencingEntity = $l.referencing
            Lookup = [ordered]@{
                '@odata.type' = 'Microsoft.Dynamics.CRM.LookupAttributeMetadata'; AttributeType = 'Lookup'; AttributeTypeName = @{ Value = 'LookupType' }
                SchemaName = $l.schema; DisplayName = (Label $l.display); Description = (Label $l.display); RequiredLevel = (Required)
            }
            AssociatedMenuConfiguration = [ordered]@{ Behavior = 'UseCollectionName'; Group = 'Details'; Label = (Label $l.display); Order = 10000 }
            CascadeConfiguration = [ordered]@{ Assign = 'NoCascade'; Delete = 'RemoveLink'; Merge = 'NoCascade'; Reparent = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade' }
        }
        [void](Invoke-Crm 'POST' 'RelationshipDefinitions' $body)
        $created += $l.relationship
    }
}
foreach ($m in $schema.manyToMany) {
    $has = Relationship-Exists $m.schema
    if ($has) { Note ("  {0}: exists" -f $m.schema); continue }
    Note ("  {0} ({1} <-> {2}, many-to-many): {3}" -f $m.schema, $m.entity1, $m.entity2, $(if ($Apply) { 'creating...' } else { 'MISSING (would create)' }))
    if ($Apply) {
        $body = [ordered]@{
            '@odata.type' = 'Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata'; SchemaName = $m.schema
            Entity1LogicalName = $m.entity1; Entity2LogicalName = $m.entity2; IntersectEntityName = $m.schema
            Entity1AssociatedMenuConfiguration = [ordered]@{ Behavior = 'UseLabel'; Group = 'Details'; Label = (Label $m.label1); Order = 10000 }
            Entity2AssociatedMenuConfiguration = [ordered]@{ Behavior = 'UseLabel'; Group = 'Details'; Label = (Label $m.label2); Order = 10000 }
        }
        [void](Invoke-Crm 'POST' 'RelationshipDefinitions' $body)
        $created += $m.schema
    }
}

if ($Apply -and $created.Count -gt 0) {
    Note "`nPublishing all customizations (can take a minute)..."
    [void](Invoke-Crm 'POST' 'PublishAllXml' @{})
}

if ($SkipData -or -not $Apply) {
    Note "`n=== 3) Data: skipped ($(if (-not $Apply) { 'dry run' } else { '-SkipData' })) ==="
}
else {
    Note "`n=== 3) Data ==="
    <#
    .SYNOPSIS
    Returns the entity set name of an entity from its logical name.
    #>
    function Set-Of([string]$logical) { (Invoke-Crm 'GET' "EntityDefinitions(LogicalName='$logical')?`$select=EntitySetName").EntitySetName }
    $sets = @{}
    foreach ($e in $schema.entities) { $sets[$e.schema] = Set-Of $e.schema }

    <#
    .SYNOPSIS
    Finds a record id by name in an entity set and returns null if none matches.
    #>
    function Find-Id([string]$set, [string]$idField, [string]$nameField, [string]$name) {
        $filter = [uri]::EscapeDataString("$nameField eq '" + $name.Replace("'", "''") + "'")
        $r = Invoke-Crm 'GET' "$set`?`$select=$idField&`$filter=$filter&`$top=1"
        if (@($r.value).Count -gt 0) { return $r.value[0].$idField } else { return $null }
    }
    <#
    .SYNOPSIS
    Returns the id of the named record, creating it first if it does not exist.
    #>
    function Ensure-Record([string]$set, [string]$idField, [string]$nameField, [string]$name, $body) {
        $id = Find-Id $set $idField $nameField $name
        if ($id) { return $id }
        $body[$nameField] = $name
        $r = Invoke-Crm 'POST' $set $body @{ Prefer = 'return=representation' }
        return $r.$idField
    }
    <#
    .SYNOPSIS
    Returns the option value for a label within an option list.
    #>
    function Opt($list, [string]$label) { 100000000 + [array]::IndexOf(@($list), $label) }

    $hasMulti = Attribute-Exists 'new_arac' 'new_ozellikler'

    $accounts = @{}
    foreach ($a in (Invoke-Crm 'GET' 'accounts?$select=accountid,name&$top=200').value) { $accounts[$a.name] = $a.accountid }

    $modelIds = @{}
    foreach ($m in $data.models) {
        $body = @{ new_marka = (Opt $data.options.marka $m.marka); new_yil = [int]$m.yil; new_kasa = (Opt $data.options.kasa $m.kasa); new_motor = [decimal]$m.motor }
        $modelIds[$m.name] = Ensure-Record $sets['new_model'] 'new_modelid' 'new_name' $m.name $body
    }
    Note "  models: $($modelIds.Count)"

    $accessoryIds = @{}
    foreach ($a in $data.accessories) { $accessoryIds[$a] = Ensure-Record $sets['new_aksesuar'] 'new_aksesuarid' 'new_name' $a @{} }
    Note "  accessories: $($accessoryIds.Count)"

    $vehicleIds = @{}
    $n = 0
    foreach ($v in $data.vehicles) {
        $body = @{
            new_yakit = (Opt $data.options.yakit $v.yakit); new_garantili = [bool]$v.garantili; new_kilometre = [int]$v.kilometre; new_alimtarihi = $v.alimTarihi
            'new_modelid@odata.bind' = "/$($sets['new_model'])($($modelIds[$v.model]))"
        }
        if ($accounts.ContainsKey($v.musteri)) { $body['new_musteriid@odata.bind'] = "/accounts($($accounts[$v.musteri]))" } else { Note "    customer not found in CRM: $($v.musteri)" }
        if ($v.aciklama) { $body['new_aciklama'] = $v.aciklama }
        if ($hasMulti -and @($v.ozellikler).Count -gt 0) { $body['new_ozellikler'] = ((@($v.ozellikler) | ForEach-Object { Opt $data.options.ozellikler $_ }) -join ',') }
        $vehicleIds[$v.plaka] = Ensure-Record $sets['new_arac'] 'new_aracid' 'new_plaka' $v.plaka $body
        $n++
        if ($n % 20 -eq 0) { Note "  vehicles: $n / $($data.vehicles.Count)" }
    }
    Note "  vehicles: $($vehicleIds.Count)"

    $n = 0
    foreach ($s in $data.services) {
        $body = @{
            new_servistarihi = $s.tarih; new_tutar = [decimal]$s.tutar; new_islemturu = (Opt $data.options.islem $s.tur); new_tamamlandi = [bool]$s.tamamlandi
            'new_aracid@odata.bind' = "/$($sets['new_arac'])($($vehicleIds[$s.plaka]))"
        }
        [void](Ensure-Record $sets['new_servis_kaydi'] 'new_servis_kaydiid' 'new_baslik' $s.baslik $body)
        $n++
        if ($n % 30 -eq 0) { Note "  service records: $n / $($data.services.Count)" }
    }
    Note "  service records: $n"

    $links = 0
    foreach ($v in $data.vehicles) {
        foreach ($accName in @($v.aksesuarlar)) {
            $vid = $vehicleIds[$v.plaka]; $aid = $accessoryIds[$accName]
            $existing = Invoke-Crm 'GET' "$($sets['new_arac'])($vid)/new_arac_aksesuar?`$select=new_aksesuarid&`$filter=new_aksesuarid eq $aid"
            if (@($existing.value).Count -eq 0) {
                [void](Invoke-Crm 'POST' "$($sets['new_arac'])($vid)/new_arac_aksesuar/`$ref" @{ '@odata.id' = "$api/$($sets['new_aksesuar'])($aid)" })
                $links++
            }
        }
    }
    Note "  new vehicle-accessory links: $links"
    if (-not $hasMulti) { Note '  NOTE: the multi-select column does not exist on this CRM build; case k07 (sunroof) must be skipped.' ; $problems += 'multi-select missing' }
}

Write-Host ''
if (-not $Apply) { Write-Host 'DRY RUN: nothing was written. To create everything: .\tools\Create-CustomTables.ps1 -Apply' }
else { Write-Host ("Done. Created {0} item(s). {1}" -f $created.Count, $(if ($problems.Count -gt 0) { 'Notes: ' + ($problems -join '; ') } else { 'No problems.' })) }
