<#
.SYNOPSIS
Injects the chat widget into every HTML page of the CRM IIS site with an outbound URL Rewrite rule; run in an admin PowerShell.
#>
param(
    [string]$ServiceUrl = 'http://localhost:5080',
    [string]$Target = 'crm.example.com',
    [switch]$Remove
)
$ErrorActionPreference = 'Stop'

$ServiceUrl = $ServiceUrl.TrimEnd('/')
if ($ServiceUrl -eq '' -or $ServiceUrl -notmatch '^(https?://[A-Za-z0-9.\-]+(:\d+)?)?(/[A-Za-z0-9._\-]+)*$') {
    throw "ServiceUrl must look like http://host:port, http://host/path or /path (letters, digits, . _ - only). Got: '$ServiceUrl'"
}
if ((Get-Service WinRM).Status -ne 'Running') { Start-Service WinRM }
$cred = Get-Credential -UserName 'CONTOSO\Administrator' -Message 'CRM app server admin (lab)'

Invoke-Command -ComputerName $Target -Credential $cred -Authentication Negotiate -ArgumentList $Remove, $ServiceUrl, $cred -ScriptBlock {
    param($Remove, $ServiceUrl, $cred)
    $siteName = 'Microsoft Dynamics CRM'
    $cfgPath  = "$env:windir\System32\inetsrv\config\applicationHost.config"
    $ruleNames = @('NlQueryNoGzip', 'NlQueryInject')

    $backup = "$cfgPath.nlquery-$(Get-Date -Format 'yyyyMMdd-HHmmss').bak"
    Copy-Item $cfgPath $backup
    "Backup: $backup"

    [xml]$xml = Get-Content $cfgPath -Raw
    $loc = $xml.SelectSingleNode("//location[@path='$siteName']")

    if ($Remove) {
        $rewrite = $null
        if ($loc) { $rewrite = $loc.SelectSingleNode('system.webServer/rewrite') }
        if (-not $rewrite) { "Nothing to remove (no <rewrite> block for '$siteName')."; return }
        $ours = $rewrite.SelectNodes('.//rule') | Where-Object { $ruleNames -contains $_.GetAttribute('name') }
        if (-not $ours) { throw "The <rewrite> block for '$siteName' has none of our rules; refusing to touch it." }
        $rewrite.ParentNode.RemoveChild($rewrite) | Out-Null
        $xml.Save($cfgPath)
        "Removed the chat widget rules. (The global allowedServerVariables entry is harmless and was left.)"
        return
    }

    if (-not $loc) {
        $loc = $xml.CreateElement('location'); $loc.SetAttribute('path', $siteName)
        $xml.configuration.AppendChild($loc) | Out-Null
    }
    $sw = $loc.SelectSingleNode('system.webServer')
    if (-not $sw) { $sw = $xml.CreateElement('system.webServer'); $loc.AppendChild($sw) | Out-Null }
    if ($sw.SelectSingleNode('rewrite')) { throw "A <rewrite> block already exists for '$siteName'; refusing to touch it." }

    $rewriteXml = @'
<rewrite>
  <rules>
    <rule name="NlQueryNoGzip" stopProcessing="false">
      <match url=".*" />
      <serverVariables><set name="HTTP_ACCEPT_ENCODING" value="" /></serverVariables>
      <action type="None" />
    </rule>
  </rules>
  <outboundRules>
    <preConditions>
      <preCondition name="NlQueryIsHtml"><add input="{RESPONSE_CONTENT_TYPE}" pattern="^text/html" /></preCondition>
    </preConditions>
    <rule name="NlQueryInject" preCondition="NlQueryIsHtml" patternSyntax="ECMAScript">
      <match filterByTags="None" pattern="&lt;/body&gt;" />
      <action type="Rewrite" value="&lt;script src=&quot;__SERVICE__/widget/widget.js&quot;&gt;&lt;/script&gt;&lt;/body&gt;" />
    </rule>
  </outboundRules>
</rewrite>
'@.Replace('__SERVICE__', $ServiceUrl)
    $frag = $xml.CreateDocumentFragment(); $frag.InnerXml = $rewriteXml
    $sw.AppendChild($frag) | Out-Null

    $sws = $xml.SelectSingleNode('/configuration/system.webServer')
    $rw  = $sws.SelectSingleNode('rewrite')
    if (-not $rw) { $rw = $xml.CreateElement('rewrite'); $sws.AppendChild($rw) | Out-Null }
    $asv = $rw.SelectSingleNode('allowedServerVariables')
    if (-not $asv) { $asv = $xml.CreateElement('allowedServerVariables'); $rw.AppendChild($asv) | Out-Null }
    if (-not $asv.SelectSingleNode("add[@name='HTTP_ACCEPT_ENCODING']")) {
        $a = $xml.CreateElement('add'); $a.SetAttribute('name', 'HTTP_ACCEPT_ENCODING'); $asv.AppendChild($a) | Out-Null
    }

    $xml.Save($cfgPath)
    "applicationHost.config updated. Widget URL: $ServiceUrl/widget/widget.js"

    try {
        $r = Invoke-WebRequest 'http://crmapp/contoso/main.aspx' -Credential $cred -UseBasicParsing -MaximumRedirection 5
        "main.aspx -> HTTP $($r.StatusCode), contains script tag: $($r.Content -match 'widget/widget.js')"
    } catch { "Self-check could not fetch main.aspx ($($_.Exception.Message)). Check in the browser instead." }
}
