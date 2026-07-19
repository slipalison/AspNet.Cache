#Requires -Version 7
$ErrorActionPreference = 'Stop'

$budgets = Get-Content (Join-Path $PSScriptRoot '..' 'benchmarks' 'budgets.json') | ConvertFrom-Json
$reportFile = Get-ChildItem -Recurse -Path 'BenchmarkDotNet.Artifacts/results' -Filter '*-report-full*.json' |
    Select-Object -First 1
if ($null -eq $reportFile) { throw 'Benchmark report not found. Run the benchmarks with --exporters json first.' }

$report = Get-Content $reportFile.FullName -Raw | ConvertFrom-Json
$failed = $false

foreach ($benchmark in $report.Benchmarks) {
    $name = $benchmark.Method
    $allocated = $benchmark.Memory.BytesAllocatedPerOperation
    $gen1 = $benchmark.Memory.Gen1Collections
    $gen2 = $benchmark.Memory.Gen2Collections
    $limit = $budgets.$name

    Write-Host ("{0}: {1} B/op (limit {2}), Gen1/1k={3}, Gen2/1k={4}" -f $name, $allocated, $limit, $gen1, $gen2)

    if ($gen2 -gt 0) { Write-Host "FAIL: $name triggered Gen2 collections"; $failed = $true }
    if ($gen1 -gt 0) { Write-Host "FAIL: $name triggered Gen1 collections"; $failed = $true }
    if ($null -ne $limit -and $allocated -gt $limit) {
        Write-Host "FAIL: $name allocated $allocated B/op, budget is $limit"
        $failed = $true
    }
}

if ($failed) { exit 1 }
Write-Host 'All allocation budgets respected.'
