$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ('.test-artifacts/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
Push-Location $root
try {
    & dotnet build Validation/NavigationChecks.csproj -c Debug --artifacts-path "$run/build" --nologo 2>&1 |
        Tee-Object -FilePath (Join-Path $run 'build.log')
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
    & dotnet "$run/build/bin/NavigationChecks/debug/NavigationChecks.dll" 2>&1 |
        Tee-Object -FilePath (Join-Path $run 'checks.log')
    if ($LASTEXITCODE -ne 0) { throw "Checks failed: $LASTEXITCODE" }
}
finally {
    Pop-Location
    Write-Host "Artifacts: $run"
}
