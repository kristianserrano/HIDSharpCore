# Runs every collision scenario in a fresh process and reports PASS / FAIL.
# Build first:  dotnet build -c Release   (from this folder)
# Then run:     powershell -ExecutionPolicy Bypass -File .\run-tests.ps1

$dll = Join-Path $PSScriptRoot "bin\Release\net8.0\HidSharp.CollisionTest.dll"
if (-not (Test-Path $dll)) { Write-Error "Build first: dotnet build -c Release"; exit 1 }

# Windows reports an unhandled .NET exception as 0xE0434352 (-532462766).
$crash = -532462766

function Run-Scenario($scenario) {
    Write-Host ""
    Write-Host "=== $scenario ===" -ForegroundColor Cyan
    & dotnet $dll $scenario
    return $LASTEXITCODE
}

$results = @()

# These must all survive with every copy initialized (exit code 0).
foreach ($s in @("patched", "stock+patched", "patched+stock", "patched+patched")) {
    $code = Run-Scenario $s
    $results += [pscustomobject]@{ Scenario = $s; Exit = $code; Result = $(if ($code -eq 0) { "PASS" } else { "FAIL" }) }
}

# Control: two STOCK copies in one process is the bug. It is expected to crash.
$code = Run-Scenario "stock+stock"
if ($code -eq 0) {
    $verdict = "NOT REPRODUCED - the control did not crash, so the PASS results above prove little on this machine"
} else {
    $verdict = "BUG REPRODUCED (expected)"
}
$results += [pscustomobject]@{ Scenario = "stock+stock (control)"; Exit = $code; Result = $verdict }

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
$results | Format-Table -AutoSize
if ($results[0..3] | Where-Object { $_.Result -ne "PASS" }) { exit 1 } else { exit 0 }
