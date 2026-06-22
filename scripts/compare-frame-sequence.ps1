param(
    [Parameter(Mandatory = $true)]
    [string]$ActualDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ReferenceDirectory,

    [string]$ActualFilter = "seq_*.bmp",

    [string]$ReferenceFilter = "seq_*.png",

    [int]$ActualXOffset = 0,

    [int]$ActualYOffset = 0
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$actualFiles = @(Get-ChildItem -LiteralPath $ActualDirectory -Filter $ActualFilter | Sort-Object Name)
$referenceFiles = @(Get-ChildItem -LiteralPath $ReferenceDirectory -Filter $ReferenceFilter | Sort-Object Name)
$count = [Math]::Min($actualFiles.Count, $referenceFiles.Count)
if ($count -eq 0) {
    throw "No frame pairs found."
}

Write-Output "index,actual,reference,compared_pixels,differing_pixels,max_channel_delta,total_absolute_channel_delta"

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
        $referenceXStart = [Math]::Max(0, $ActualXOffset)
        $referenceYStart = [Math]::Max(0, $ActualYOffset)
        $referenceXEnd = [Math]::Min($reference.Width, $actual.Width + $ActualXOffset)
        $referenceYEnd = [Math]::Min($reference.Height, $actual.Height + $ActualYOffset)
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
                }

                $maxChannelDelta = [Math]::Max($maxChannelDelta, [Math]::Max($dr, [Math]::Max($dg, $db)))
                $totalAbsoluteChannelDelta += $dr + $dg + $db
            }
        }

        Write-Output "$i,$($actualFiles[$i].Name),$($referenceFiles[$i].Name),$comparedPixels,$differingPixels,$maxChannelDelta,$totalAbsoluteChannelDelta"
    }
    finally {
        $actual.Dispose()
        $reference.Dispose()
    }
}
