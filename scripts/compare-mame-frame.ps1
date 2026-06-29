param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$RomPath,

    [Parameter(Mandatory = $true)]
    [int]$Frame,

    [string]$InputScript = "",

    [string]$OutDir = ".\artifacts\frame-compare",

    [string]$MamePath = ".\tools\mame-0.288\mame.exe",

    [string]$ProjectPath = ".\src\NesSharp.Cli\NesSharp.Cli.csproj",

    [string]$Configuration = "Release",

    [string]$System = "nes",

    [long]$MaxInstructions = 50000000,

    [int]$ScanRadius = 0,

    [int]$OffsetRadius = 8,

    [int]$ActualXOffset = 2,

    [int]$ActualYOffset = 0,

    [int]$Hotspots = 0,

    [switch]$ExactRgb,

    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

if ($Frame -lt 1) {
    throw "Frame must be 1 or greater."
}

if ($ScanRadius -lt 0) {
    throw "ScanRadius must be 0 or greater."
}

if ($OffsetRadius -lt 0) {
    throw "OffsetRadius must be 0 or greater."
}

if ($Hotspots -lt 0) {
    throw "Hotspots must be 0 or greater."
}

$resolvedRomPath = Resolve-Path -LiteralPath $RomPath
$outputDirectory = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$romBaseName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedRomPath.Path)
$safeRomBaseName = $romBaseName -replace '[^A-Za-z0-9._-]+', '_'
$artifactBaseName = "$safeRomBaseName-frame$Frame"
$referencePath = Join-Path $outputDirectory "$artifactBaseName-mame.bmp"
$actualPath = Join-Path $outputDirectory "$artifactBaseName-nessharp.bmp"
$diffPath = Join-Path $outputDirectory "$artifactBaseName-diff.bmp"
$compareLogPath = Join-Path $outputDirectory "$artifactBaseName-compare.txt"
$scanLogPath = Join-Path $outputDirectory "$artifactBaseName-scan.txt"

$captureScriptPath = Join-Path $PSScriptRoot "capture-mame-frame.ps1"
& $captureScriptPath `
    -RomPath $resolvedRomPath.Path `
    -Frame $Frame `
    -OutPath $referencePath `
    -MamePath $MamePath `
    -SnapshotDirectory (Join-Path $outputDirectory "mame-snapshots") `
    -InputScript $InputScript `
    -System $System

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (-not $NoBuild) {
    dotnet build $ProjectPath -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$compareArgs = New-Object System.Collections.Generic.List[string]
$compareArgs.Add("run")
$compareArgs.Add("-c")
$compareArgs.Add($Configuration)
if ($NoBuild) {
    $compareArgs.Add("--no-build")
}

$compareArgs.Add("--project")
$compareArgs.Add($ProjectPath)
$compareArgs.Add("--")
$compareArgs.Add("compare-frame")
$compareArgs.Add($resolvedRomPath.Path)
$compareArgs.Add("--frames")
$compareArgs.Add($Frame.ToString([System.Globalization.CultureInfo]::InvariantCulture))
$compareArgs.Add("--reference")
$compareArgs.Add($referencePath)
$compareArgs.Add("--out")
$compareArgs.Add($actualPath)
$compareArgs.Add("--diff-out")
$compareArgs.Add($diffPath)
$compareArgs.Add("--max-instructions")
$compareArgs.Add($MaxInstructions.ToString([System.Globalization.CultureInfo]::InvariantCulture))
if ($OffsetRadius -gt 0) {
    $compareArgs.Add("--offset-radius")
    $compareArgs.Add($OffsetRadius.ToString([System.Globalization.CultureInfo]::InvariantCulture))
}

if ($ActualXOffset -ne 0) {
    $compareArgs.Add("--actual-x-offset")
    $compareArgs.Add($ActualXOffset.ToString([System.Globalization.CultureInfo]::InvariantCulture))
}

if ($ActualYOffset -ne 0) {
    $compareArgs.Add("--actual-y-offset")
    $compareArgs.Add($ActualYOffset.ToString([System.Globalization.CultureInfo]::InvariantCulture))
}

if ($Hotspots -gt 0) {
    $compareArgs.Add("--hotspots")
    $compareArgs.Add($Hotspots.ToString([System.Globalization.CultureInfo]::InvariantCulture))
}

if (-not $ExactRgb) {
    $compareArgs.Add("--normalize-palette")
}

if (-not [string]::IsNullOrWhiteSpace($InputScript)) {
    $compareArgs.Add("--input")
    $compareArgs.Add($InputScript)
}

$compareOutput = dotnet @compareArgs 2>&1
$compareExitCode = $LASTEXITCODE
$compareOutput | Tee-Object -FilePath $compareLogPath

Write-Output "Reference frame: $referencePath"
Write-Output "nesSharp frame:  $actualPath"
Write-Output "Diff frame:      $diffPath"
Write-Output "Compare log:     $compareLogPath"

if ($ScanRadius -gt 0) {
    $startFrame = [Math]::Max(1, $Frame - $ScanRadius)
    $endFrame = $Frame + $ScanRadius
    $scanArgs = New-Object System.Collections.Generic.List[string]
    $scanArgs.Add("run")
    $scanArgs.Add("-c")
    $scanArgs.Add($Configuration)
    if ($NoBuild) {
        $scanArgs.Add("--no-build")
    }

    $scanArgs.Add("--project")
    $scanArgs.Add($ProjectPath)
    $scanArgs.Add("--")
    $scanArgs.Add("scan-frame-match")
    $scanArgs.Add($resolvedRomPath.Path)
    $scanArgs.Add("--reference")
    $scanArgs.Add($referencePath)
    $scanArgs.Add("--start-frame")
    $scanArgs.Add($startFrame.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    $scanArgs.Add("--end-frame")
    $scanArgs.Add($endFrame.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    $scanArgs.Add("--max-instructions")
    $scanArgs.Add($MaxInstructions.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    if ($ActualXOffset -ne 0) {
        $scanArgs.Add("--actual-x-offset")
        $scanArgs.Add($ActualXOffset.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    }

    if ($ActualYOffset -ne 0) {
        $scanArgs.Add("--actual-y-offset")
        $scanArgs.Add($ActualYOffset.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    }

    if (-not $ExactRgb) {
        $scanArgs.Add("--normalize-palette")
    }

    if (-not [string]::IsNullOrWhiteSpace($InputScript)) {
        $scanArgs.Add("--input")
        $scanArgs.Add($InputScript)
    }

    $scanOutput = dotnet @scanArgs 2>&1
    $scanOutput | Tee-Object -FilePath $scanLogPath
    Write-Output "Scan log:        $scanLogPath"
}

exit $compareExitCode
