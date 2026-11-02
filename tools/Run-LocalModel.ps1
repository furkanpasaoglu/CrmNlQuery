<#
.SYNOPSIS
Runs the eval packs one after another against the local Ollama model, for the lab only.
#>
param(
    [string]$BaseUrl = 'http://crm.example.com/contoso',
    [string]$Model = 'qwen3.6:27b',
    [int]$Repeat = 1,
    [string[]]$Only = @(),
    [string]$Label = '',
    [string[]]$EvalArgs = @()
)
$ErrorActionPreference = 'Stop'
if ($env:NLQUERY_LAB_CRM -ne '1') { throw 'This script writes test data or runs evaluations against a CRM. Set $env:NLQUERY_LAB_CRM = ''1'' only when the CRM is a lab or test system.' }
Set-Location (Split-Path $PSScriptRoot -Parent)

try { $tags = Invoke-RestMethod -Uri 'http://localhost:11434/api/tags' -TimeoutSec 5 }
catch { throw 'Ollama is not reachable at http://localhost:11434. Start Ollama and run again.' }
$names = @($tags.models | ForEach-Object { $_.name })
if (-not ($names | Where-Object { $_ -eq $Model -or $_ -like "$Model*" })) { throw "Model '$Model' is not installed in Ollama (installed: $($names -join ', '))." }
if (-not ($names | Where-Object { $_ -like 'bge-m3*' })) { Write-Host 'Warning: bge-m3 is not installed: the knowledge packs will fail. Use -Only to pick packs without knowledge, or install it.' -ForegroundColor Yellow }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$tag = if ($Label) { '-' + ($Label -replace '[^A-Za-z0-9]+', '') } else { '' }
$logDir = Join-Path 'eval\results' "local-$stamp$tag"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$summaryFile = Join-Path $logDir 'summary.txt'

$crm = Get-Credential -UserName 'CONTOSO\Administrator' -Message 'Lab CRM password (kept only in this PowerShell window)'
$envBefore = $env:Crm__Password
try {
    $env:Crm__Password = $crm.GetNetworkCredential().Password
    Write-Host "`n=== Build ===" -ForegroundColor Cyan
    dotnet build src/CrmNlQuery.Eval -v q | Select-Object -Last 2
    if ($LASTEXITCODE -ne 0) { throw 'Eval build failed.' }
    Remove-Item -Recurse -Force 'metadata-cache' -ErrorAction SilentlyContinue

    $common = @('--provider', 'ollama', '--model', $Model, '--repeat', $Repeat) + @($EvalArgs)
    $packs = @(
        @{ Name = 'speed (12)';              Args = @('--cases', 'eval/speed-cases.json', '--knowledge', 'on') },
        @{ Name = 'cases (38)';              Args = @('--cases', 'eval/cases.json', '--knowledge', 'on') },
        @{ Name = 'customer summary (10)';   Args = @('--cases', 'eval/summary-cases.json', '--summary', 'on') },
        @{ Name = 'closed data (12)';        Args = @('--cases', 'eval/closed-field-cases.json', '--policy', 'eval/policy-lab.json') },
        @{ Name = 'context (6)';             Args = @('--cases', 'eval/context-cases.json', '--knowledge', 'on') },
        @{ Name = 'chains (7)';              Args = @('--chain-cases', 'eval/chain-cases.json', '--knowledge', 'on') },
        @{ Name = 'turkish names (7)';       Args = @('--cases', 'eval/turkish-name-cases.json', '--knowledge', 'on') },
        @{ Name = 'one-sentence (6)';        Args = @('--cases', 'eval/oneshot-cases.json', '--knowledge', 'on') },
        @{ Name = 'custom tables (10)';      Args = @('--cases', 'eval/custom-cases.json') },
        @{ Name = 'knowledge (9)';           Args = @('--cases', 'eval/knowledge-cases.json', '--knowledge', 'on') },
        @{ Name = 'multi-step golden (19)';  Args = @('--cases', 'eval/multistep-cases.json', '--knowledge', 'on') },
        @{ Name = 'context+chain (1)';       Args = @('--chain-cases', 'eval/context-chain-cases.json', '--knowledge', 'on') },
        @{ Name = 'splitter (decompose)';    Args = @('--decompose-cases', 'eval/multistep-cases.json') }
    )
    if ($Only.Count -gt 0) { $packs = @($packs | Where-Object { $name = $_.Name; @($Only | Where-Object { $name -like "*$_*" }).Count -gt 0 }) }

    "Local model run $stamp, model $Model, repeat $Repeat, label '$Label', extra args: $($EvalArgs -join ' ')" | Out-File -FilePath $summaryFile -Encoding ascii
    $ollamaEnv = @(Get-ChildItem Env: | Where-Object { $_.Name -like 'Ollama__*' } | ForEach-Object { "$($_.Name)=$($_.Value)" })
    ("Ollama settings from the environment: " + $(if ($ollamaEnv.Count -gt 0) { $ollamaEnv -join ', ' } else { '(none, defaults)' })) | Out-File -FilePath $summaryFile -Append -Encoding ascii
    $total = Get-Date
    $n = 0
    foreach ($p in $packs) {
        $n++
        $file = Join-Path $logDir ("{0:D2}-{1}.txt" -f $n, ($p.Name -replace '[^A-Za-z0-9]+', '-').Trim('-'))
        Write-Host ("`n--- {0} ({1}/{2}) ---" -f $p.Name, $n, $packs.Count) -ForegroundColor Cyan
        $started = Get-Date
        $runArgs = @('run', '--project', 'src/CrmNlQuery.Eval', '--no-build', '--') + @($p.Args) + $common
        dotnet @runArgs 2>&1 | Tee-Object -FilePath $file | ForEach-Object { if ($_ -match '^(FAIL|FLAKY)') { Write-Host $_ -ForegroundColor Yellow } }
        $lines = @(Get-Content $file | Where-Object { $_ -match '\d+/\d+' })
        $last = if ($lines.Count -gt 0) { $lines[-1].Trim() } else { '(no result line; see the log)' }
        $minutes = [math]::Round(((Get-Date) - $started).TotalMinutes, 1)
        $slow = @(Get-Content $file | Where-Object { $_ -match '\((\d+)s\)' } | Where-Object { [int]([regex]::Match($_, '\((\d+)s\)').Groups[1].Value) -gt 120 })
        $line = "{0} | {1} | {2} min | cases over 120 s: {3}" -f $p.Name, $last, $minutes, $slow.Count
        Write-Host $line -ForegroundColor Green
        $line | Out-File -FilePath $summaryFile -Append -Encoding ascii
        $slow | ForEach-Object { ("    slow: " + $_.Trim()) | Out-File -FilePath $summaryFile -Append -Encoding ascii }
        Get-Content $file | Where-Object { $_ -match '^(FAIL|FLAKY)' } | ForEach-Object { ("    " + $_.Trim()) | Out-File -FilePath $summaryFile -Append -Encoding ascii }
    }
    $all = [math]::Round(((Get-Date) - $total).TotalMinutes, 1)
    "TOTAL: $all minutes" | Out-File -FilePath $summaryFile -Append -Encoding ascii
    Write-Host "`n=== Summary ===" -ForegroundColor Green
    Get-Content $summaryFile | Write-Host
    Write-Host "Logs: $logDir  (paste summary.txt to the assistant)"
}
finally {
    $env:Crm__Password = $envBefore
}
