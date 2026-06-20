param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$RomPath,

    [Parameter(Mandatory = $true)]
    [int]$Frame,

    [Parameter(Mandatory = $true)]
    [string]$OutPath,

    [string]$MamePath = ".\tools\mame-0.288\mame.exe",

    [string]$SnapshotDirectory = ".\artifacts\mame",

    [string]$System = "nes"
)

$ErrorActionPreference = "Stop"

if ($Frame -lt 1) {
    throw "Frame must be 1 or greater."
}

$resolvedMamePath = Resolve-Path -LiteralPath $MamePath
$resolvedRomPath = Resolve-Path -LiteralPath $RomPath
$snapshotDirectoryPath = [System.IO.Path]::GetFullPath($SnapshotDirectory)
$outputPath = [System.IO.Path]::GetFullPath($OutPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($outputPath)
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

New-Item -ItemType Directory -Force -Path $snapshotDirectoryPath | Out-Null
$workDirectory = Join-Path $snapshotDirectoryPath "work"
$cfgDirectory = Join-Path $workDirectory "cfg"
$nvramDirectory = Join-Path $workDirectory "nvram"
$stateDirectory = Join-Path $workDirectory "sta"
$inputDirectory = Join-Path $workDirectory "inp"
New-Item -ItemType Directory -Force -Path $cfgDirectory, $nvramDirectory, $stateDirectory, $inputDirectory | Out-Null

$nesNtscFrameRate = 60.0988138974405
$seconds = [Math]::Max(1, [int][Math]::Ceiling($Frame / $nesNtscFrameRate))
$secondsText = $seconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$snapshotName = [System.IO.Path]::GetFileNameWithoutExtension($outputPath)
$snapshotPng = Join-Path $snapshotDirectoryPath "$snapshotName.png"
if (Test-Path -LiteralPath $snapshotPng) {
    Remove-Item -LiteralPath $snapshotPng -Force
}

& $resolvedMamePath.Path $System `
    -cart $resolvedRomPath.Path `
    -skip_gameinfo `
    -nothrottle `
    -sound none `
    -video gdi `
    -window `
    -cfg_directory $cfgDirectory `
    -nvram_directory $nvramDirectory `
    -state_directory $stateDirectory `
    -input_directory $inputDirectory `
    -snapshot_directory $snapshotDirectoryPath `
    -snapname $snapshotName `
    -snapsize 256x240 `
    -snapview native `
    -nosnapbilinear `
    -seconds_to_run $secondsText

if ($LASTEXITCODE -ne 0) {
    throw "MAME exited with code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $snapshotPng)) {
    throw "MAME did not produce expected snapshot '$snapshotPng'."
}

$extension = [System.IO.Path]::GetExtension($outputPath).ToLowerInvariant()
switch ($extension) {
    ".png" {
        Copy-Item -LiteralPath $snapshotPng -Destination $outputPath -Force
    }
    ".bmp" {
        Add-Type -AssemblyName System.Drawing
        $bitmap = [System.Drawing.Bitmap]::new($snapshotPng)
        try {
            $bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
        }
        finally {
            $bitmap.Dispose()
        }
    }
    ".ppm" {
        Add-Type -AssemblyName System.Drawing
        $bitmap = [System.Drawing.Bitmap]::new($snapshotPng)
        try {
            if ($bitmap.Width -ne 256 -or $bitmap.Height -ne 240) {
                throw "MAME snapshot must be 256x240, got $($bitmap.Width)x$($bitmap.Height)."
            }

            $stream = [System.IO.File]::Create($outputPath)
            try {
                $header = [System.Text.Encoding]::ASCII.GetBytes("P6`n256 240`n255`n")
                $stream.Write($header, 0, $header.Length)
                $pixel = [byte[]]::new(3)
                for ($y = 0; $y -lt 240; $y++) {
                    for ($x = 0; $x -lt 256; $x++) {
                        $color = $bitmap.GetPixel($x, $y)
                        $pixel[0] = $color.R
                        $pixel[1] = $color.G
                        $pixel[2] = $color.B
                        $stream.Write($pixel, 0, $pixel.Length)
                    }
                }
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }
    default {
        throw "OutPath must end in .bmp, .ppm, or .png."
    }
}

Write-Output "Captured MAME frame $Frame to $outputPath"
