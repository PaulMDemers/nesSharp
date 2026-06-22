param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$RomPath,

    [Parameter(Mandatory = $true)]
    [int]$StartFrame,

    [Parameter(Mandatory = $true)]
    [int]$EndFrame,

    [Parameter(Mandatory = $true)]
    [string]$OutFile,

    [string]$MamePath = ".\tools\mame-0.288\mame.exe",

    [string]$InputScript = "",

    [string]$System = "nes"
)

$ErrorActionPreference = "Stop"

if ($StartFrame -lt 1 -or $EndFrame -lt $StartFrame) {
    throw "Frame range must satisfy 1 <= StartFrame <= EndFrame."
}

$resolvedMamePath = Resolve-Path -LiteralPath $MamePath
$resolvedRomPath = Resolve-Path -LiteralPath $RomPath
$outputPath = [System.IO.Path]::GetFullPath($OutFile)
$outputDirectory = Split-Path -Parent $outputPath
$workDirectory = Join-Path $outputDirectory "work"
$cfgDirectory = Join-Path $workDirectory "cfg"
$nvramDirectory = Join-Path $workDirectory "nvram"
$stateDirectory = Join-Path $workDirectory "sta"
$inputDirectory = Join-Path $workDirectory "inp"

New-Item -ItemType Directory -Force -Path $outputDirectory, $workDirectory, $cfgDirectory, $nvramDirectory, $stateDirectory, $inputDirectory | Out-Null

$traceScriptPath = Join-Path $workDirectory "trace-writes-$StartFrame-$EndFrame.lua"
$nesNtscFrameRate = 60.0988138974405
$watchdogSeconds = [Math]::Max(1, [int][Math]::Ceiling($EndFrame / $nesNtscFrameRate) + 2)
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
        $start = [int]::Parse($framePieces[0], [System.Globalization.CultureInfo]::InvariantCulture)
        $stop = if ($framePieces.Length -eq 1) {
            $start
        }
        else {
            [int]::Parse($framePieces[1], [System.Globalization.CultureInfo]::InvariantCulture)
        }

        if ($start -lt 0 -or $stop -lt $start) {
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

        $ranges.Add("{ start = $start, stop = $stop, buttons = { $($buttons -join ", ") } }")
    }

    return "{`n    $($ranges -join ",`n    ")`n}"
}

$luaInputRanges = Convert-InputScriptToLuaTable $InputScript

$traceScript = @"
local start_frame = $StartFrame
local end_frame = $EndFrame
local input_ranges = $luaInputRanges
local button_fields = {}
local button_names = { "A", "B", "Select", "Start", "Up", "Down", "Left", "Right" }
local cpu = manager.machine.devices[":maincpu"]
local mem = cpu.spaces["program"]

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

local function should_trace(address)
    return ((address >= 0x2000 and address <= 0x3fff) or address == 0x4014 or address >= 0x8000)
end

local function normalise_ppu_address(address)
    if address >= 0x2000 and address <= 0x3fff then
        return 0x2000 + (address & 0x0007)
    end
    return address
end

local function log_access(kind, address, data)
    if _G.trace_frame < start_frame or _G.trace_frame > end_frame or not should_trace(address) then
        return
    end

    local pc = cpu.state["PC"].value
    print(string.format("F%4d PC=%04X $%04X%s$%02X", _G.trace_frame, pc, normalise_ppu_address(address), kind, data & 0xff))
end

_G.trace_frame = 0
bind_inputs()

_G.trace_write_tap_ppu = mem:install_write_tap(0x2000, 0x4014, "nes_pu_writes", function(offset, data, mask)
    log_access("<-", offset, data)
end)
_G.trace_write_tap_mapper = mem:install_write_tap(0x8000, 0xffff, "nes_mapper_writes", function(offset, data, mask)
    log_access("<-", offset, data)
end)
_G.trace_read_tap_status = mem:install_read_tap(0x2002, 0x2002, "nes_status_reads", function(offset, data, mask)
    log_access("->", offset, data)
end)

_G.trace_frame_sub = emu.add_machine_frame_notifier(function()
    _G.trace_frame = _G.trace_frame + 1
    apply_inputs(_G.trace_frame)
    if _G.trace_frame > end_frame then
        manager.machine:exit()
    end
end)
"@
Set-Content -LiteralPath $traceScriptPath -Value $traceScript -Encoding ASCII

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
    -autoboot_script $traceScriptPath 2>&1 |
    Where-Object { $_ -match '^F\s*\d+' } |
    Set-Content -LiteralPath $outputPath -Encoding ASCII

if ($LASTEXITCODE -ne 0) {
    throw "MAME exited with code $LASTEXITCODE."
}

Write-Output "Captured MAME write trace $StartFrame-$EndFrame to $outputPath"
