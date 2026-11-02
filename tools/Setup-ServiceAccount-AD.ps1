<#
.SYNOPSIS
Creates the NL Query service account in Active Directory and denies interactive logon on the app server.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$DomainController = 'dc.example.com',
    [string]$AppServer = 'crm.example.com',
    [string]$Domain = 'CONTOSO',
    [string]$Name = 'svc-nlquery'
)
$ErrorActionPreference = 'Stop'
$account = "$Domain\$Name"

if ((Get-Service WinRM).Status -ne 'Running') { Start-Service WinRM }

<#
.SYNOPSIS
Opens a remote PowerShell session to the given computer, with notes on the TrustedHosts requirement for IP addresses.
#>
function New-RemoteSession([string]$Computer) {
    try { New-PSSession -ComputerName $Computer -Credential $cred -Authentication Negotiate }
    catch {
        Write-Warning "Could not connect to $Computer. If it is an IP address, add it to TrustedHosts first (admin PowerShell, once):"
        Write-Warning "  Set-Item WSMan:\localhost\Client\TrustedHosts -Value '$Computer' -Concatenate -Force"
        throw
    }
}
$cred = Get-Credential -UserName "$Domain\Administrator" -Message 'Domain admin (used only for the remote sessions)'

$dc = New-RemoteSession $DomainController
try {
    $exists = Invoke-Command -Session $dc -ArgumentList $Name -ScriptBlock {
        param($n)
        Import-Module ActiveDirectory
        [bool](Get-ADUser -Filter "SamAccountName -eq '$n'" -ErrorAction SilentlyContinue)
    }
    if ($exists) {
        "AD account $account already exists: not touched (the password is not reset)."
    }
    elseif ($PSCmdlet.ShouldProcess("$account on $DomainController", 'Create AD user')) {
        $p1 = Read-Host -Prompt "New password for $account (hidden)" -AsSecureString
        $p2 = Read-Host -Prompt 'Type it again' -AsSecureString
        $b1 = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($p1)
        $b2 = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($p2)
        try {
            $same = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b1) -ceq [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b2)
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b1)
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b2)
        }
        if (-not $same -or $p1.Length -lt 12) { throw 'The two entries differ, or the password is shorter than 12 characters.' }
        Invoke-Command -Session $dc -ArgumentList $Name, $p1 -ScriptBlock {
            param($n, $pw)
            Import-Module ActiveDirectory
            $domainDns = (Get-ADDomain).DNSRoot
            New-ADUser -Name $n -SamAccountName $n -UserPrincipalName "$n@$domainDns" `
                -Description 'NL Query service account (application pool identity; no interactive logon)' `
                -AccountPassword $pw -Enabled $true -PasswordNeverExpires $true -CannotChangePassword $true
        }
        "Created AD account $account (enabled, password never expires, cannot be changed by the user)."
    }
}
finally { Remove-PSSession $dc -ErrorAction SilentlyContinue }

$app = New-RemoteSession $AppServer
try {
    if ($PSCmdlet.ShouldProcess("$AppServer local security policy", "Deny interactive and remote-desktop logon for $account")) {
        $result = Invoke-Command -Session $app -ArgumentList $Domain, $Name -ScriptBlock {
            param($d, $n)
            $sid = (New-Object Security.Principal.NTAccount($d, $n)).Translate([Security.Principal.SecurityIdentifier]).Value
            $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
            $backup = "C:\Windows\Temp\nlquery-userrights-$stamp-before.inf"
            $work = "C:\Windows\Temp\nlquery-userrights-$stamp-new.inf"
            $db = "C:\Windows\Temp\nlquery-userrights-$stamp.sdb"
            & secedit /export /cfg $backup /areas USER_RIGHTS | Out-Null
            $lines = [System.Collections.Generic.List[string]](Get-Content $backup)
            foreach ($right in 'SeDenyInteractiveLogonRight', 'SeDenyRemoteInteractiveLogonRight') {
                $idx = -1
                for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match "^\s*$right\s*=") { $idx = $i; break } }
                if ($idx -ge 0) {
                    if ($lines[$idx] -notmatch [regex]::Escape($sid)) { $lines[$idx] = $lines[$idx].TrimEnd() + ",*$sid" }
                }
                else {
                    $section = $lines.IndexOf('[Privilege Rights]')
                    if ($section -lt 0) { throw 'The exported policy has no [Privilege Rights] section.' }
                    $lines.Insert($section + 1, "$right = *$sid")
                }
            }
            Set-Content -Path $work -Value $lines -Encoding Unicode
            & secedit /configure /db $db /cfg $work /areas USER_RIGHTS | Out-Null
            Remove-Item $db -ErrorAction SilentlyContinue
            $check = "C:\Windows\Temp\nlquery-userrights-$stamp-after.inf"
            & secedit /export /cfg $check /areas USER_RIGHTS | Out-Null
            $after = Get-Content $check
            $ok = @('SeDenyInteractiveLogonRight', 'SeDenyRemoteInteractiveLogonRight') | ForEach-Object {
                $r = $_
                $line = $after | Where-Object { $_ -match "^\s*$r\s*=" }
                "$r contains the account: $([bool]($line -match [regex]::Escape($sid)))"
            }
            Remove-Item $check, $work -ErrorAction SilentlyContinue
            $ok + "Previous policy export kept at $backup"
        }
        $result
    }
}
finally { Remove-PSSession $app -ErrorAction SilentlyContinue }

""
"Next: .\Setup-ServiceAccount-Crm.ps1 (CRM role and non-interactive user), then .\Check-ServiceAccount.ps1."
