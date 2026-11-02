<#
.SYNOPSIS
Runs a lab-only load test of the local model with N simultaneous virtual users and reports timing per user count.
#>
param(
    [string]$BaseUrl = 'http://crm.example.com/contoso',
    [string]$Model = 'qwen3.6:27b',
    [int[]]$Users = @(1, 3, 5),
    [int]$Rounds = 1,
    [string]$Cases = 'eval/speed-cases.json',
    [string]$Label = '',
    [int]$TargetSimple = 30,
    [int]$TargetMulti = 60
)
$ErrorActionPreference = 'Stop'
if ($env:NLQUERY_LAB_CRM -ne '1') { throw 'This script writes test data or runs evaluations against a CRM. Set $env:NLQUERY_LAB_CRM = ''1'' only when the CRM is a lab or test system.' }
Set-Location (Split-Path $PSScriptRoot -Parent)

try { $tags = Invoke-RestMethod -Uri 'http://localhost:11434/api/tags' -TimeoutSec 5 }
catch { throw 'Ollama is not reachable at http://localhost:11434. Start Ollama and run again.' }
$names = @($tags.models | ForEach-Object { $_.name })
if (-not ($names | Where-Object { $_ -eq $Model -or $_ -like "$Model*" })) { throw "Model '$Model' is not installed in Ollama (installed: $($names -join ', '))." }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$tag = if ($Label) { '-' + ($Label -replace '[^A-Za-z0-9]+', '') } else { '' }
$summaryFile = Join-Path 'eval\results' "load-$stamp$tag.txt"
New-Item -ItemType Directory -Force -Path 'eval\results' | Out-Null

$crm = Get-Credential -UserName 'CONTOSO\Administrator' -Message 'Lab CRM password (kept only in this PowerShell window)'
$envBefore = $env:Crm__Password
try {
    $env:Crm__Password = $crm.GetNetworkCredential().Password
    Write-Host "`n=== Build ===" -ForegroundColor Cyan
    dotnet build src/CrmNlQuery.Eval -v q | Select-Object -Last 2
    if ($LASTEXITCODE -ne 0) { throw 'Eval build failed.' }
    Remove-Item -Recurse -Force 'metadata-cache' -ErrorAction SilentlyContinue

    $ollamaEnv = @(Get-ChildItem Env: | Where-Object { $_.Name -like 'Ollama__*' -or $_.Name -like 'OLLAMA_*' } | ForEach-Object { "$($_.Name)=$($_.Value)" })
    "Load test $stamp, model $Model, users $($Users -join ','), rounds $Rounds, cases $Cases, label '$Label'" | Out-File -FilePath $summaryFile -Encoding ascii
    ("Ollama settings from this window's environment: " + $(if ($ollamaEnv.Count -gt 0) { $ollamaEnv -join ', ' } else { '(none, defaults)' })) | Out-File -FilePath $summaryFile -Append -Encoding ascii
    "(The Ollama SERVER settings such as OLLAMA_NUM_PARALLEL belong to the process that started Ollama: write them in -Label.)" | Out-File -FilePath $summaryFile -Append -Encoding ascii

    $runArgs = @('run', '--project', 'src/CrmNlQuery.Eval', '--no-build', '--', '--provider', 'ollama', '--model', $Model, '--cases', $Cases, '--users', ($Users -join ','), '--rounds', $Rounds, '--target-simple', $TargetSimple, '--target-multi', $TargetMulti)
    dotnet @runArgs 2>&1 | Tee-Object -FilePath $summaryFile -Append
    Write-Host "`nSummary file: $summaryFile" -ForegroundColor Green
}
finally {
    if ($null -eq $envBefore) { Remove-Item Env:Crm__Password -ErrorAction SilentlyContinue } else { $env:Crm__Password = $envBefore }
}
