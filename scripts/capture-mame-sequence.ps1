param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$RomPath,

    [Parameter(Mandatory = $true)]
    [int]$StartFrame,

    [Parameter(Mandatory = $true)]
    [int]$EndFrame,

    [int]$Step = 1,

    [Parameter(Mandatory = $true)]
    [string]$OutDirectory,

    [string]$MamePath = ".\tools\mame-0.288\mame.exe",

    [string]$System = "nes"
)

$ErrorActionPreference = "Stop"

if ($StartFrame -lt 1 -or $EndFrame -lt $StartFrame -or $Step -lt 1) {
    throw "Frame range must satisfy 1 <= StartFrame <= EndFrame and Step >= 1."
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

$captureScriptPath = Join-Path $workDirectory "capture-sequence-$StartFrame-$EndFrame.lua"
$nesNtscFrameRate = 60.0988138974405
$watchdogSeconds = [Math]::Max(1, [int][Math]::Ceiling($EndFrame / $nesNtscFrameRate) + 2)
$watchdogSecondsText = $watchdogSeconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)

$captureScript = @"
local start_frame = $StartFrame
local end_frame = $EndFrame
local step = $Step
_G.capture_sequence_frame = 0
_G.capture_sequence_sub = emu.add_machine_frame_notifier(function()
    _G.capture_sequence_frame = _G.capture_sequence_frame + 1
    local frame = _G.capture_sequence_frame
    if frame >= start_frame and frame <= end_frame and ((frame - start_frame) % step) == 0 then
        print("capturing " .. frame)
        manager.machine.video:snapshot()
    end
    if frame > end_frame then
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
    -snapshot_directory $outputDirectoryPath `
    -snapname "seq_%i" `
    -snapsize 256x240 `
    -snapview native `
    -nosnapbilinear `
    -seconds_to_run $watchdogSecondsText `
    -autoboot_delay 0 `
    -autoboot_script $captureScriptPath

if ($LASTEXITCODE -ne 0) {
    throw "MAME exited with code $LASTEXITCODE."
}

Write-Output "Captured MAME frames $StartFrame-$EndFrame step $Step to $outputDirectoryPath"
