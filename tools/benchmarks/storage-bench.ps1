# Storage benchmark for ticket 12 / O-22 (storage designed for scale).
#
# Builds the StorageBench console project and runs it against throwaway
# databases under %TEMP%. It never starts Shiyu.App and never touches the
# real data directory.
#
# Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\benchmarks\storage-bench.ps1

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

# PSCommandPath is ...\tools\benchmarks\storage-bench.ps1: three levels up is the repo.
$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSCommandPath))
$project = Join-Path $repo "tools\benchmarks\StorageBench.csproj"

Write-Host "[bench] building $Configuration..."
dotnet build $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "build failed"
    exit 1
}

Write-Host "[bench] running..."
dotnet run --project $project -c $Configuration --no-build
exit $LASTEXITCODE
