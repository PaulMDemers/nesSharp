param(
    [Parameter(Mandatory = $true)]
    [string]$NesSharpStateDirectory,

    [Parameter(Mandatory = $true)]
    [string]$MameStateDirectory,

    [ValidateSet("Horizontal", "Vertical", "OneScreenLower", "OneScreenUpper")]
    [string]$Mirroring = "Horizontal"
)

$ErrorActionPreference = "Stop"

function Read-RequiredBytes {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing state file: $Path"
    }

    return [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Path).Path)
}

function Get-PhysicalNametableOffset {
    param(
        [int]$LogicalOffset,
        [string]$Mode
    )

    $table = [int][Math]::Floor($LogicalOffset / 0x0400)
    $inner = $LogicalOffset - ($table * 0x0400)
    $physicalTable = switch ($Mode) {
        "Horizontal" { [int][Math]::Floor($table / 2) }
        "Vertical" { $table -band 0x01 }
        "OneScreenLower" { 0 }
        "OneScreenUpper" { 1 }
    }

    return $physicalTable * 0x0400 + $inner
}

function Write-DiffSummary {
    param(
        [string]$Name,
        [byte[]]$Actual,
        [byte[]]$Reference,
        [int]$BaseAddress
    )

    $count = [Math]::Min($Actual.Length, $Reference.Length)
    $diffs = New-Object System.Collections.Generic.List[int]
    for ($i = 0; $i -lt $count; $i++) {
        if ($Actual[$i] -ne $Reference[$i]) {
            $diffs.Add($i)
        }
    }

    Write-Output "$Name diffs: $($diffs.Count)"
    for ($i = 0; $i -lt [Math]::Min(24, $diffs.Count); $i++) {
        $index = $diffs[$i]
        Write-Output ("  {0:X4}: nesSharp={1:X2} MAME={2:X2}" -f ($BaseAddress + $index), $Actual[$index], $Reference[$index])
    }
}

$nesNametable = Read-RequiredBytes (Join-Path $NesSharpStateDirectory "nametable.bin")
$nesPalette = Read-RequiredBytes (Join-Path $NesSharpStateDirectory "palette.bin")
$nesOam = Read-RequiredBytes (Join-Path $NesSharpStateDirectory "oam.bin")
$mameNametableLogical = Read-RequiredBytes (Join-Path $MameStateDirectory "nametable-logical.bin")
$mamePaletteLogical = Read-RequiredBytes (Join-Path $MameStateDirectory "palette-logical.bin")
$mameCpuOamPage = Read-RequiredBytes (Join-Path $MameStateDirectory "cpu-0200.bin")

if ($nesNametable.Length -ne 0x0800) {
    throw "nesSharp nametable.bin must be 2048 bytes."
}

if ($mameNametableLogical.Length -ne 0x1000) {
    throw "MAME nametable-logical.bin must be 4096 bytes."
}

$nesNametableLogical = New-Object byte[] 0x1000
for ($i = 0; $i -lt $nesNametableLogical.Length; $i++) {
    $nesNametableLogical[$i] = $nesNametable[(Get-PhysicalNametableOffset $i $Mirroring)]
}

$nesPaletteLogical = [byte[]]$nesPalette.Clone()
foreach ($mirror in @(0x10, 0x14, 0x18, 0x1C)) {
    $nesPaletteLogical[$mirror] = $nesPaletteLogical[$mirror - 0x10]
}

Write-DiffSummary "nametable" $nesNametableLogical $mameNametableLogical 0x2000
Write-DiffSummary "palette" $nesPaletteLogical $mamePaletteLogical 0x3F00
Write-DiffSummary "oam/cpu-0200" $nesOam $mameCpuOamPage 0x0000
