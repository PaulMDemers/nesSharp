param(
    [string]$OutPath = ".\docs\compatibility-dashboard.md",

    [string]$Configuration = "Release",

    [switch]$IncludeSlow,

    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $root $OutPath))
$outputDirectory = [System.IO.Path]::GetDirectoryName($outputPath)
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

function Invoke-RepoCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string[]]$Command
    )

    $started = Get-Date
    $executable = $Command[0]
    $arguments = if ($Command.Length -gt 1) { $Command[1..($Command.Length - 1)] } else { @() }
    $output = & $executable @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $elapsed = (Get-Date) - $started

    [pscustomobject]@{
        Name = $Name
        Command = $Command -join " "
        ExitCode = $exitCode
        ElapsedSeconds = [Math]::Round($elapsed.TotalSeconds, 1)
        Output = ($output | ForEach-Object { $_.ToString() })
    }
}

function Format-CommandStatus {
    param([pscustomobject]$Result)

    $status = if ($Result.ExitCode -eq 0) { "pass" } else { "fail ($($Result.ExitCode))" }
    $command = $Result.Command.Replace("|", "\|")
    return "| $($Result.Name) | $status | $($Result.ElapsedSeconds)s | ``$command`` |"
}

function Get-SprDmaScore {
    param([pscustomobject]$Result)

    $scoreLine = $Result.Output | Where-Object { $_ -match '^Diff score: abs=(\d+) max=(\d+)$' } | Select-Object -Last 1
    if ($scoreLine -match '^Diff score: abs=(\d+) max=(\d+)$') {
        return [pscustomobject]@{
            Abs = [int]$Matches[1]
            Max = [int]$Matches[2]
        }
    }

    return [pscustomobject]@{
        Abs = $null
        Max = $null
    }
}

Push-Location $root
try {
    $results = New-Object System.Collections.Generic.List[object]

    if (-not $NoBuild) {
        $results.Add((Invoke-RepoCommand "Release build" @("dotnet", "build", "src\NesSharp.Cli\NesSharp.Cli.csproj", "-c", $Configuration)))
    }

    $results.Add((Invoke-RepoCommand "DMA/APU focused tests" @(
        "dotnet",
        "test",
        "tests\NesSharp.Tests\NesSharp.Tests.csproj",
        "--filter",
        "ApuBusTests|CpuBusTests|OamDmcDmaTimingTests|DmcDmaDuringReadRomTests|SprDmaOutputParserExtractsRowsAndScoresDiffs",
        "--logger",
        "console;verbosity=minimal")))

    $sprDmaNormal = Invoke-RepoCommand "sprdma normal" @(
        "dotnet",
        "run",
        "-c",
        $Configuration,
        "--no-build",
        "--project",
        "src\NesSharp.Cli",
        "--",
        "sprdma-report",
        "test-roms\nes-test-roms\sprdma_and_dmc_dma\sprdma_and_dmc_dma.nes")
    $results.Add($sprDmaNormal)

    $sprDma512 = Invoke-RepoCommand "sprdma 512" @(
        "dotnet",
        "run",
        "-c",
        $Configuration,
        "--no-build",
        "--project",
        "src\NesSharp.Cli",
        "--",
        "sprdma-report",
        "test-roms\nes-test-roms\sprdma_and_dmc_dma\sprdma_and_dmc_dma_512.nes")
    $results.Add($sprDma512)

    if ($IncludeSlow) {
        $results.Add((Invoke-RepoCommand "instr_timing aggregate" @(
            "dotnet",
            "run",
            "-c",
            $Configuration,
            "--no-build",
            "--project",
            "src\NesSharp.Cli",
            "--",
            "test-rom",
            "test-roms\nes-test-roms\instr_timing\instr_timing.nes")))

        $results.Add((Invoke-RepoCommand "instr_test all_instrs" @(
            "dotnet",
            "run",
            "-c",
            $Configuration,
            "--no-build",
            "--project",
            "src\NesSharp.Cli",
            "--",
            "test-rom",
            "test-roms\nes-test-roms\instr_test-v5\all_instrs.nes")))
    }

    $normalScore = Get-SprDmaScore $sprDmaNormal
    $score512 = Get-SprDmaScore $sprDma512
    $commit = (git rev-parse --short HEAD).Trim()
    $branch = (git rev-parse --abbrev-ref HEAD).Trim()
    $timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss zzz", [System.Globalization.CultureInfo]::InvariantCulture)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Compatibility Dashboard")
    $lines.Add("")
    $lines.Add("Generated: $timestamp")
    $lines.Add("")
    $lines.Add("- Branch: ``$branch``")
    $lines.Add("- Commit: ``$commit``")
    $lines.Add("- Configuration: ``$Configuration``")
    $lines.Add("- Slow checks: ``$IncludeSlow``")
    $lines.Add("")
    $lines.Add("## Headline Scores")
    $lines.Add("")
    $lines.Add("| Area | Current | Notes |")
    $lines.Add("| --- | ---: | --- |")
    $lines.Add("| sprdma_and_dmc_dma | abs=$($normalScore.Abs), max=$($normalScore.Max) | Normal one-byte sample/OAM overlap table. |")
    $lines.Add("| sprdma_and_dmc_dma_512 | abs=$($score512.Abs), max=$($score512.Max) | Late OAM-window DMC reload edge remains the main known gap. |")
    $lines.Add("")
    $lines.Add("## Command Results")
    $lines.Add("")
    $lines.Add("| Check | Status | Time | Command |")
    $lines.Add("| --- | --- | ---: | --- |")
    foreach ($result in $results) {
        $lines.Add((Format-CommandStatus $result))
    }

    $lines.Add("")
    $lines.Add("## Next Debugging Targets")
    $lines.Add("")
    $lines.Add("- Use ``scripts\compare-mame-frame.ps1`` for retail visual captures and diffs.")
    $lines.Add("- Continue narrowing ``sprdma_and_dmc_dma_512`` rows with immediate post-OAM DMC reloads.")
    $lines.Add("- Add any newly failing retail smoke cases here with frame number, input script, and artifact paths.")
    $lines.Add("")

    Set-Content -LiteralPath $outputPath -Value $lines -Encoding ASCII
    Write-Output "Wrote $outputPath"
}
finally {
    Pop-Location
}
