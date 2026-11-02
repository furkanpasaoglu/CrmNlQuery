<#
.SYNOPSIS
Runs the whole check against a lab CRM: unit tests and all eval packs. Seed the lab first with Seed-EvalData.ps1.
#>
param(
    [string]$BaseUrl = 'http://crm.example.com/contoso',
    [string]$Model = 'claude-haiku-5-5',
    [int]$Repeat = 1,
    [string[]]$Only = @(),
    [switch]$NoPrune,
    [switch]$Parallel,
    [switch]$SkipEval,
    [switch]$AcceptData
)
$ErrorActionPreference = 'Stop'
if ($env:NLQUERY_LAB_CRM -ne '1') { throw 'This script writes test data or runs evaluations against a CRM. Set $env:NLQUERY_LAB_CRM = ''1'' only when the CRM is a lab or test system.' }
Set-Location (Split-Path $PSScriptRoot -Parent)

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logDir = Join-Path 'eval\results' "regression-$stamp"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

$crm = Get-Credential -UserName 'CONTOSO\Administrator' -Message 'Lab CRM password (kept only in this PowerShell window)'
$envBefore = @{ Crm = $env:Crm__Password; Key = $env:Anthropic__ApiKey }
try {
    Write-Host "`n=== 1) Unit tests ===" -ForegroundColor Cyan
    dotnet test --filter "Category!=Live" | Tee-Object -FilePath (Join-Path $logDir '1-unit-tests.txt') | Select-Object -Last 3
    if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed. See the log above; nothing else was run.' }

    if (-not $SkipEval) {
        $keyCred = Get-Credential -UserName 'anthropic-api-key' -Message 'Anthropic API key (put it in the Password field)'
        $env:Crm__Password = $crm.GetNetworkCredential().Password
        $env:Anthropic__ApiKey = $keyCred.GetNetworkCredential().Password
        Write-Host "`n=== 4) Eval packs ($Model, repeat $Repeat) ===" -ForegroundColor Cyan
        dotnet build src/CrmNlQuery.Eval -v q | Select-Object -Last 2
        if ($LASTEXITCODE -ne 0) { throw 'Eval build failed.' }

        Remove-Item -Recurse -Force 'metadata-cache' -ErrorAction SilentlyContinue
        $common = @('--provider', 'anthropic', '--model', $Model, '--repeat', $Repeat)
        if ($NoPrune) { $common += @('--prune', 'off') }
        if ($AcceptData) { $common += @('--accept-data') }
        $packs = @(
            @{ Name = 'cases (38)';              Args = @('--cases', 'eval/cases.json', '--knowledge', 'on') },
            @{ Name = 'chains (7)';              Args = @('--chain-cases', 'eval/chain-cases.json', '--knowledge', 'on') },
            @{ Name = 'context (6)';             Args = @('--cases', 'eval/context-cases.json', '--knowledge', 'on') },
            @{ Name = 'context+chain (1)';       Args = @('--chain-cases', 'eval/context-chain-cases.json', '--knowledge', 'on') },
            @{ Name = 'multi-step golden (19)';  Args = @('--cases', 'eval/multistep-cases.json', '--knowledge', 'on') },
            @{ Name = 'splitter (decompose)';    Args = @('--decompose-cases', 'eval/multistep-cases.json') },
            @{ Name = 'knowledge (9)';           Args = @('--cases', 'eval/knowledge-cases.json', '--knowledge', 'on') },
            @{ Name = 'knowledge realistic (11)'; Args = @('--cases', 'eval/knowledge-realistic-cases.json', '--knowledge', 'on', '--knowledge-dir', 'eval/knowledge-realistic') },
            @{ Name = 'one-sentence (6)';        Args = @('--cases', 'eval/oneshot-cases.json', '--knowledge', 'on') },
            @{ Name = 'turkish names (6)';       Args = @('--cases', 'eval/turkish-name-cases.json', '--knowledge', 'on') },
            @{ Name = 'custom tables (10)';      Args = @('--cases', 'eval/custom-cases.json') },
            @{ Name = 'customer summary (10)';   Args = @('--cases', 'eval/summary-cases.json', '--summary', 'on') },
            @{ Name = 'business glossary (6)';   Args = @('--cases', 'eval/glossary-cases.json', '--glossary-entries', 'eval/glossary-entries.json') },
            @{ Name = 'report finder (12)';      Args = @('--report-cases', 'eval/report-cases.json') },
            @{ Name = 'email draft (10)';        Args = @('--cases', 'eval/draft-cases.json', '--draft', 'on') },
            @{ Name = 'closed data (12)';        Args = @('--cases', 'eval/closed-field-cases.json', '--policy', 'eval/policy-lab.json') },
            @{ Name = 'date grouping (6)';       Args = @('--cases', 'eval/date-group-cases.json') },
            @{ Name = 'chart intent (9)';        Args = @('--cases', 'eval/chart-intent-cases.json') },
            @{ Name = 'design assistant (4)';    Args = @('--design-cases', 'eval/design-cases.json') },
            @{ Name = 'record creation (14)';    Args = @('--create-cases', 'eval/create-cases.json') }
        )
        if (Test-Path 'eval\generated\lab-tr.json') { $packs += @{ Name = 'generated lab set'; Args = @('--cases', 'eval/generated/lab-tr.json') } }
        if ($Only.Count -gt 0) { $packs = @($packs | Where-Object { $name = $_.Name; @($Only | Where-Object { $name -like "*$_*" }).Count -gt 0 }) }
        $needsOllama = @($packs | Where-Object { $_.Args -contains 'on' -and $_.Args -contains '--knowledge' }).Count -gt 0
        if ($needsOllama) {
            try { [void](Invoke-RestMethod -Uri 'http://localhost:11434/api/version' -TimeoutSec 3) }
            catch { throw 'Ollama is not reachable at http://localhost:11434. The knowledge packs need it (embedding model bge-m3). Start Ollama and run again, or pick packs without knowledge with -Only.' }
        }
        $results = @()
        $n = 0
        if ($Parallel) {
            $absoluteLogDir = (Resolve-Path $logDir).Path
            $jobs = @()
            foreach ($p in $packs) {
                $n++
                $file = Join-Path $absoluteLogDir ("4-{0:D2}-{1}.txt" -f $n, ($p.Name -replace '[^A-Za-z0-9]+', '-').Trim('-'))
                $runArgs = @('run', '--project', 'src/CrmNlQuery.Eval', '--no-build', '--') + @($p.Args) + $common
                $job = Start-Job -ArgumentList (Get-Location).Path, $runArgs, $file -ScriptBlock { param($dir, $a, $f) Set-Location $dir; & dotnet @a 2>&1 | Out-File -FilePath $f -Encoding utf8 }
                $jobs += [pscustomobject]@{ Pack = $p.Name; File = $file; Job = $job }
            }
            Write-Host ("Started {0} packs in parallel; waiting..." -f $jobs.Count)
            [void](Wait-Job -Job ($jobs | ForEach-Object { $_.Job }))
            foreach ($j in $jobs) {
                Get-Content $j.File | Where-Object { $_ -match '^(FAIL|FLAKY|DATA-MISSING|  DATA-CHANGED|  LOW-SIGNAL)' } | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
                $lines = @(Get-Content $j.File | Where-Object { $_ -match '\d+/\d+' })
                $last = if ($lines.Count -gt 0) { $lines[-1].Trim() } else { '(no result line; see the log)' }
                $results += [pscustomobject]@{ Pack = $j.Pack; Result = $last; Minutes = [math]::Round(($j.Job.PSEndTime - $j.Job.PSBeginTime).TotalMinutes, 1) }
            }
            $jobs | ForEach-Object { Remove-Job -Job $_.Job -Force }
            $packs = @()
        }
        foreach ($p in $packs) {
            $n++
            $file = Join-Path $logDir ("4-{0:D2}-{1}.txt" -f $n, ($p.Name -replace '[^A-Za-z0-9]+', '-').Trim('-'))
            Write-Host ("`n--- {0} ---" -f $p.Name) -ForegroundColor Cyan
            $started = Get-Date
            $runArgs = @('run', '--project', 'src/CrmNlQuery.Eval', '--no-build', '--') + @($p.Args) + $common
            dotnet @runArgs | Tee-Object -FilePath $file | ForEach-Object { if ($_ -match '^(FAIL|FLAKY|DATA-MISSING|  DATA-CHANGED|  LOW-SIGNAL)') { Write-Host $_ -ForegroundColor Yellow } }
            $lines = @(Get-Content $file | Where-Object { $_ -match '\d+/\d+' })
            $last = if ($lines.Count -gt 0) { $lines[-1].Trim() } else { '(no result line; see the log)' }
            $results += [pscustomobject]@{ Pack = $p.Name; Result = $last; Minutes = [math]::Round(((Get-Date) - $started).TotalMinutes, 1) }
        }
        Write-Host "`n=== Result per pack ===" -ForegroundColor Green
        $results | Format-Table -AutoSize -Wrap | Out-String | Write-Host
    }
    Write-Host "Logs: $logDir  (paste the 'Result per pack' table and any FAIL/FLAKY/DATA-MISSING/DATA-CHANGED lines to the assistant)"
}
finally {
    $env:Crm__Password = $envBefore.Crm
    $env:Anthropic__ApiKey = $envBefore.Key
}
