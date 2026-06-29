param(
    [string]$Configuration = "Debug",

    [switch]$NoBuild,

    [switch]$IncludeSprDma,

    [switch]$IncludeSlow
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$testProject = "tests\NesSharp.Tests\NesSharp.Tests.csproj"
$logger = "console;verbosity=minimal"
$failures = New-Object System.Collections.Generic.List[string]

function Invoke-CompatCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string[]]$Command
    )

    Write-Host ""
    Write-Host "== $Name ==" -ForegroundColor Cyan
    Write-Host ($Command -join " ")

    $started = Get-Date
    & $Command[0] $Command[1..($Command.Length - 1)]
    $exitCode = $LASTEXITCODE
    $elapsed = (Get-Date) - $started

    if ($exitCode -ne 0) {
        $script:failures.Add("$Name failed with exit code $exitCode after $([Math]::Round($elapsed.TotalSeconds, 1))s.")
    }
    else {
        Write-Host "Passed in $([Math]::Round($elapsed.TotalSeconds, 1))s." -ForegroundColor Green
    }
}

function Invoke-TestGroup {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Filter
    )

    Invoke-CompatCommand $Name @(
        "dotnet",
        "test",
        $testProject,
        "-c",
        $Configuration,
        "--no-build",
        "--filter",
        $Filter,
        "--logger",
        $logger)
}

Push-Location $root
try {
    if (-not $NoBuild) {
        Invoke-CompatCommand "Build tests" @(
            "dotnet",
            "build",
            $testProject,
            "-c",
            $Configuration)
    }

    Invoke-TestGroup "CPU/APU/DMA focused tests" (
        "FullyQualifiedName~ApuBusTests|" +
        "FullyQualifiedName~CpuBusTests|" +
        "FullyQualifiedName~CpuDmaSchedulerTests|" +
        "FullyQualifiedName~OamDmcDmaTimingTests|" +
        "FullyQualifiedName~DmcDmaDuringReadRomTests|" +
        "FullyQualifiedName~SprDmaOutputParserExtractsRowsAndScoresDiffs")

    Invoke-TestGroup "Mapper 4 compatibility tests" "FullyQualifiedName~Mmc3"

    Invoke-TestGroup "PPU compatibility tests" "FullyQualifiedName~Ppu"

    Invoke-TestGroup "Quick ROM and regression tests" (
        "FullyQualifiedName~ControllerTests|" +
        "FullyQualifiedName~CartridgeTests|" +
        "FullyQualifiedName~FrameRegressionTests|" +
        "FullyQualifiedName~ReadJoyRomTests|" +
        "FullyQualifiedName~VblNmiTimingRomTests")

    Invoke-TestGroup "Quick CPU timing tests" (
        "FullyQualifiedName~BranchTimingRomTests|" +
        "FullyQualifiedName~NestestTraceTests")

    if ($IncludeSprDma) {
        $previousSprDma = $env:NESSHARP_RUN_SPRDMA
        $env:NESSHARP_RUN_SPRDMA = "1"
        try {
            Invoke-TestGroup "Opt-in SPR-DMA/DMC timing guards" (
                "FullyQualifiedName~SprDmaAndDmcDmaReportsCurrentTimingWhenEnabled|" +
                "FullyQualifiedName~SprDmaAndDmcDma512ReportsCurrentTimingWhenEnabled")
        }
        finally {
            if ($null -eq $previousSprDma) {
                Remove-Item Env:\NESSHARP_RUN_SPRDMA -ErrorAction SilentlyContinue
            }
            else {
                $env:NESSHARP_RUN_SPRDMA = $previousSprDma
            }
        }
    }

    if ($IncludeSlow) {
        Write-Host ""
        Write-Host "Running slow aggregate CPU/instruction ROM suites." -ForegroundColor Yellow
        Invoke-TestGroup "CPU timing ROM suite" "FullyQualifiedName~CpuTimingRomTests"
        Invoke-TestGroup "Blargg instruction ROM suite" "FullyQualifiedName~BlarggInstructionRomTests"
    }

    if ($failures.Count -gt 0) {
        Write-Host ""
        Write-Host "Compatibility test failures:" -ForegroundColor Red
        foreach ($failure in $failures) {
            Write-Host "- $failure" -ForegroundColor Red
        }

        exit 1
    }

    Write-Host ""
    Write-Host "Compatibility test groups passed." -ForegroundColor Green
}
finally {
    Pop-Location
}
