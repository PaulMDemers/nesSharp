param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$RomPath,

    [Parameter(Mandatory = $true)]
    [int]$Frame,

    [Parameter(Mandatory = $true)]
    [string]$OutPath,

    [string]$MamePath = ".\tools\mame-0.288\mame.exe",

    [string]$SnapshotDirectory = ".\artifacts\mame",

    [string]$InputScript = "",

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

$snapshotName = [System.IO.Path]::GetFileNameWithoutExtension($outputPath)
$snapshotPng = Join-Path $snapshotDirectoryPath "$snapshotName.png"
if (Test-Path -LiteralPath $snapshotPng) {
    Remove-Item -LiteralPath $snapshotPng -Force
}

function Convert-InputScriptToLuaTable {
    param([string]$Script)

    if ([string]::IsNullOrWhiteSpace($Script)) {
        return "{}"
    }

    $validButtons = @("A", "B", "Select", "Start", "Up", "Down", "Left", "Right")
    $ranges = New-Object System.Collections.Generic.List[string]
    foreach ($part in $Script.Split(";", [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $pieces = $part.Split(":", 2, [System.StringSplitOptions]::TrimEntries)
        if ($pieces.Length -ne 2) {
            throw "Input ranges must use the form start-end:Button+Button."
        }

        $framePieces = $pieces[0].Split("-", 2, [System.StringSplitOptions]::TrimEntries)
        $startFrame = [int]::Parse($framePieces[0], [System.Globalization.CultureInfo]::InvariantCulture)
        $endFrame = if ($framePieces.Length -eq 1) {
            $startFrame
        }
        else {
            [int]::Parse($framePieces[1], [System.Globalization.CultureInfo]::InvariantCulture)
        }

        if ($startFrame -lt 0 -or $endFrame -lt $startFrame) {
            throw "Input range end frame cannot be before the start frame."
        }

        $buttons = New-Object System.Collections.Generic.List[string]
        foreach ($buttonText in $pieces[1].Split("+", [System.StringSplitOptions]::RemoveEmptyEntries -bor [System.StringSplitOptions]::TrimEntries)) {
            if ($buttonText -eq "0" -or $buttonText.Equals("None", [System.StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            $button = $validButtons | Where-Object { $_.Equals($buttonText, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
            if ([string]::IsNullOrWhiteSpace($button)) {
                throw "Unknown input button '$buttonText'. Valid buttons: $($validButtons -join ', ')."
            }

            $buttons.Add("`"$button`"")
        }

        $ranges.Add("{ start = $startFrame, stop = $endFrame, buttons = { $($buttons -join ", ") } }")
    }

    return "{`n    $($ranges -join ",`n    ")`n}"
}

$captureScriptPath = Join-Path $workDirectory "capture-frame-$Frame.lua"
$nesNtscFrameRate = 60.0988138974405
$watchdogSeconds = [Math]::Max(1, [int][Math]::Ceiling($Frame / $nesNtscFrameRate) + 2)
$watchdogSecondsText = $watchdogSeconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$luaInputRanges = Convert-InputScriptToLuaTable $InputScript
$captureScript = @"
local target_frame = $Frame
local frame = 0
local captured = false
local input_ranges = $luaInputRanges
local button_fields = {}
local button_names = { "A", "B", "Select", "Start", "Up", "Down", "Left", "Right" }

local function bind_inputs()
    local port = manager.machine.ioport.ports[":ctrl1:joypad:JOYPAD"]
    if port == nil then
        return
    end

    for _, field in pairs(port.fields) do
        for _, button in ipairs(button_names) do
            if field.name == ("P1 " .. button) then
                button_fields[button] = field
            end
        end
    end
end

local function apply_inputs(current_frame)
    for _, field in pairs(button_fields) do
        field:set_value(0)
    end

    for _, range in ipairs(input_ranges) do
        if current_frame >= range.start and current_frame <= range.stop then
            for _, button in ipairs(range.buttons) do
                local field = button_fields[button]
                if field ~= nil then
                    field:set_value(1)
                end
            end
        end
    end
end

bind_inputs()

emu.add_machine_frame_notifier(function()
    frame = frame + 1
    apply_inputs(frame)
    if (not captured) and frame >= target_frame then
        captured = true
        manager.machine.video:snapshot()
        manager.machine:exit()
    end
end)
"@
Set-Content -LiteralPath $captureScriptPath -Value $captureScript -Encoding ASCII

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
    -seconds_to_run $watchdogSecondsText `
    -autoboot_delay 0 `
    -autoboot_script $captureScriptPath

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
