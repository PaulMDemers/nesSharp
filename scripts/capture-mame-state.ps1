param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$RomPath,

    [Parameter(Mandatory = $true)]
    [int]$Frame,

    [Parameter(Mandatory = $true)]
    [string]$OutDirectory,

    [string]$MamePath = ".\tools\mame-0.288\mame.exe",

    [string]$InputScript = "",

    [string]$System = "nes"
)

$ErrorActionPreference = "Stop"

if ($Frame -lt 1) {
    throw "Frame must be >= 1."
}

$resolvedMamePath = Resolve-Path -LiteralPath $MamePath
$resolvedRomPath = Resolve-Path -LiteralPath $RomPath
$outputDirectoryPath = [System.IO.Path]::GetFullPath($OutDirectory)
$workDirectory = Join-Path $outputDirectoryPath "work"
$cfgDirectory = Join-Path $workDirectory "cfg"
$nvramDirectory = Join-Path $workDirectory "nvram"
$stateDirectory = Join-Path $workDirectory "sta"
$inputDirectory = Join-Path $workDirectory "inp"

if (Test-Path -LiteralPath $outputDirectoryPath) {
    Remove-Item -LiteralPath $outputDirectoryPath -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $outputDirectoryPath, $workDirectory, $cfgDirectory, $nvramDirectory, $stateDirectory, $inputDirectory | Out-Null

$captureScriptPath = Join-Path $workDirectory "capture-state-$Frame.lua"
$nesNtscFrameRate = 60.0988138974405
$watchdogSeconds = [Math]::Max(1, [int][Math]::Ceiling($Frame / $nesNtscFrameRate) + 2)
$watchdogSecondsText = $watchdogSeconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)

function Convert-InputScriptToLuaTable {
    param([string]$Script)

    if ([string]::IsNullOrWhiteSpace($Script)) {
        return "{}"
    }

    $validButtons = @("A", "B", "Select", "Start", "Up", "Down", "Left", "Right")
    $ranges = New-Object System.Collections.Generic.List[string]
    foreach ($part in $Script.Trim().Split(";", [System.StringSplitOptions]::RemoveEmptyEntries)) {
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
            throw "Input range end frame cannot be before the start."
        }

        $buttons = New-Object System.Collections.Generic.List[string]
        foreach ($rawButtonText in $pieces[1].Split("+", [System.StringSplitOptions]::RemoveEmptyEntries)) {
            $buttonText = $rawButtonText.Trim()
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

$luaInputRanges = Convert-InputScriptToLuaTable $InputScript
$luaOutputDirectory = $outputDirectoryPath.Replace("\", "/")

$captureScript = @"
local target_frame = $Frame
local out_dir = "$luaOutputDirectory"
local input_ranges = $luaInputRanges
local button_fields = {}
local button_names = { "A", "B", "Select", "Start", "Up", "Down", "Left", "Right" }
local cpu = manager.machine.devices[":maincpu"]
local cpu_program = cpu.spaces["program"]
local ppu = manager.machine.devices[":ppu"]
local ppu_videoram = ppu.spaces["videoram"]

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

local function dump(path, space, start_addr, length)
    local file = assert(io.open(path, "wb"))
    for i = 0, length - 1 do
        file:write(string.char(space:read_u8(start_addr + i)))
    end

    file:close()
end

_G.capture_state_frame = 0
bind_inputs()
_G.capture_state_sub = emu.add_machine_frame_notifier(function()
    _G.capture_state_frame = _G.capture_state_frame + 1
    apply_inputs(_G.capture_state_frame)
    if _G.capture_state_frame == target_frame then
        dump(out_dir .. "/nametable-logical.bin", ppu_videoram, 0x2000, 0x1000)
        dump(out_dir .. "/palette-logical.bin", ppu_videoram, 0x3f00, 0x0020)
        dump(out_dir .. "/cpu-0200.bin", cpu_program, 0x0200, 0x0100)
        print("captured state " .. target_frame)
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
    -video none `
    -cfg_directory $cfgDirectory `
    -nvram_directory $nvramDirectory `
    -state_directory $stateDirectory `
    -input_directory $inputDirectory `
    -seconds_to_run $watchdogSecondsText `
    -autoboot_delay 0 `
    -autoboot_script $captureScriptPath

if ($LASTEXITCODE -ne 0) {
    throw "MAME exited with code $LASTEXITCODE."
}

Write-Output "Captured MAME state at frame $Frame to $outputDirectoryPath"
