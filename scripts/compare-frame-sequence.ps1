param(
    [Parameter(Mandatory = $true)]
    [string]$ActualDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ReferenceDirectory,

    [string]$ActualFilter = "seq_*.bmp",

    [string]$ReferenceFilter = "seq_*.png",

    [int]$ActualXOffset = 0,

    [int]$ActualYOffset = 0,

    [int]$Hotspots = 0,

    [int]$CellSize = 16
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$actualFiles = @(Get-ChildItem -LiteralPath $ActualDirectory -Filter $ActualFilter | Sort-Object Name)
$referenceFiles = @(Get-ChildItem -LiteralPath $ReferenceDirectory -Filter $ReferenceFilter | Sort-Object Name)
$count = [Math]::Min($actualFiles.Count, $referenceFiles.Count)
if ($count -eq 0) {
    throw "No frame pairs found."
}

if ($Hotspots -lt 0) {
    throw "Hotspots must be 0 or greater."
}

if ($CellSize -lt 1) {
    throw "CellSize must be 1 or greater."
}

function Format-NullableInt {
    param($Value)

    if ($null -eq $Value) {
        return ""
    }

    return $Value.ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Hotspots {
    param(
        [int[]]$DifferingByCell,
        [Int64[]]$AbsoluteByCell,
        [int]$Columns,
        [int]$Rows,
        [int]$ImageWidth,
        [int]$ImageHeight,
        [int]$MaxCount,
        [int]$Size
    )

    if ($MaxCount -le 0) {
        return ""
    }

    $cells = New-Object System.Collections.Generic.List[object]
    for ($row = 0; $row -lt $Rows; $row++) {
        for ($column = 0; $column -lt $Columns; $column++) {
            $cellIndex = $row * $Columns + $column
            if ($DifferingByCell[$cellIndex] -eq 0) {
                continue
            }

            $x = $column * $Size
            $y = $row * $Size
            $cells.Add([pscustomobject]@{
                X = $x
                Y = $y
                MaxX = [Math]::Min($ImageWidth - 1, $x + $Size - 1)
                MaxY = [Math]::Min($ImageHeight - 1, $y + $Size - 1)
                Differing = $DifferingByCell[$cellIndex]
                Absolute = $AbsoluteByCell[$cellIndex]
            })
        }
    }

    return (($cells |
        Sort-Object @{ Expression = "Absolute"; Descending = $true }, @{ Expression = "Differing"; Descending = $true } |
        Select-Object -First $MaxCount |
        ForEach-Object { "$($_.X),$($_.Y)..$($_.MaxX),$($_.MaxY):$($_.Differing):$($_.Absolute)" }) -join ";")
}

Write-Output "index,actual,reference,compared_pixels,differing_pixels,min_x,min_y,max_x,max_y,max_channel_delta,total_absolute_channel_delta,hotspots"

for ($i = 0; $i -lt $count; $i++) {
    $actual = [System.Drawing.Bitmap]::new($actualFiles[$i].FullName)
    $reference = [System.Drawing.Bitmap]::new($referenceFiles[$i].FullName)
    try {
        if ($actual.Width -ne $reference.Width -or $actual.Height -ne $reference.Height) {
            throw "Frame dimensions differ for pair $i`: actual $($actual.Width)x$($actual.Height), reference $($reference.Width)x$($reference.Height)."
        }

        $differingPixels = 0
        $comparedPixels = 0
        $maxChannelDelta = 0
        [Int64]$totalAbsoluteChannelDelta = 0
        $minX = $null
        $minY = $null
        $maxX = $null
        $maxY = $null
        $referenceXStart = [Math]::Max(0, $ActualXOffset)
        $referenceYStart = [Math]::Max(0, $ActualYOffset)
        $referenceXEnd = [Math]::Min($reference.Width, $actual.Width + $ActualXOffset)
        $referenceYEnd = [Math]::Min($reference.Height, $actual.Height + $ActualYOffset)
        $columns = [Math]::Ceiling($reference.Width / [double]$CellSize)
        $rows = [Math]::Ceiling($reference.Height / [double]$CellSize)
        $differingByCell = New-Object int[] ($columns * $rows)
        $absoluteByCell = New-Object Int64[] ($columns * $rows)
        for ($y = $referenceYStart; $y -lt $referenceYEnd; $y++) {
            for ($x = $referenceXStart; $x -lt $referenceXEnd; $x++) {
                $a = $actual.GetPixel($x - $ActualXOffset, $y - $ActualYOffset)
                $r = $reference.GetPixel($x, $y)
                $dr = [Math]::Abs([int]$a.R - [int]$r.R)
                $dg = [Math]::Abs([int]$a.G - [int]$r.G)
                $db = [Math]::Abs([int]$a.B - [int]$r.B)
                $comparedPixels++
                if ($dr -ne 0 -or $dg -ne 0 -or $db -ne 0) {
                    $differingPixels++
                    $minX = if ($null -eq $minX) { $x } else { [Math]::Min($minX, $x) }
                    $minY = if ($null -eq $minY) { $y } else { [Math]::Min($minY, $y) }
                    $maxX = if ($null -eq $maxX) { $x } else { [Math]::Max($maxX, $x) }
                    $maxY = if ($null -eq $maxY) { $y } else { [Math]::Max($maxY, $y) }
                }

                $maxChannelDelta = [Math]::Max($maxChannelDelta, [Math]::Max($dr, [Math]::Max($dg, $db)))
                $absoluteDelta = $dr + $dg + $db
                $totalAbsoluteChannelDelta += $absoluteDelta
                if ($dr -ne 0 -or $dg -ne 0 -or $db -ne 0) {
                    $cellIndex = [int]([Math]::Floor($y / $CellSize) * $columns + [Math]::Floor($x / $CellSize))
                    $differingByCell[$cellIndex]++
                    $absoluteByCell[$cellIndex] += $absoluteDelta
                }
            }
        }

        $hotspotText = Format-Hotspots $differingByCell $absoluteByCell $columns $rows $reference.Width $reference.Height $Hotspots $CellSize
        Write-Output "$i,$($actualFiles[$i].Name),$($referenceFiles[$i].Name),$comparedPixels,$differingPixels,$(Format-NullableInt $minX),$(Format-NullableInt $minY),$(Format-NullableInt $maxX),$(Format-NullableInt $maxY),$maxChannelDelta,$totalAbsoluteChannelDelta,`"$hotspotText`""
    }
    finally {
        $actual.Dispose()
        $reference.Dispose()
    }
}
