# nesSharp

A C# NES emulator project with a headless test harness, early desktop host, CPU coverage, pragmatic PPU rendering, and initial common mapper support.

## Current Commands

```powershell
dotnet test NesSharp.slnx
dotnet build NesSharp.slnx
dotnet run --project src\NesSharp.Cli -- info test-roms\nes-test-roms\other\nestest.nes
dotnet run --project src\NesSharp.Cli -- nestest test-roms\nes-test-roms\other\nestest.nes test-roms\nes-test-roms\other\nestest.log
dotnet run --project src\NesSharp.Cli -- test-rom test-roms\nes-test-roms\instr_test-v5\rom_singles\01-basics.nes
dotnet run --project src\NesSharp.Cli -- test-rom test-roms\nes-test-roms\ppu_vbl_nmi\rom_singles\01-vbl_basics.nes
dotnet run --project src\NesSharp.Cli -- render-frame test-roms\nes-test-roms\ppu_read_buffer\test_ppu_read_buffer.nes --frames 120 --out artifacts\frames\ppu_read_buffer_frame120.ppm
dotnet run --project src\NesSharp.Cli -- render-frame "roms\USA\Super Mario Bros 3 (U) (PRG 1).nes" --frames 420 --input "60-90:Start;180-240:Start;260-420:Right+B" --out artifacts\frames\smb3_frame420_nessharp.bmp
.\scripts\capture-mame-frame.ps1 "roms\USA\Super Mario Bros 3 (U) (PRG 1).nes" -Frame 420 -InputScript "60-90:Start;180-240:Start;260-420:Right+B" -OutPath artifacts\mame\smb3_frame420.bmp
dotnet run --project src\NesSharp.Cli -- compare-frame "roms\USA\Super Mario Bros 3 (U) (PRG 1).nes" --frames 420 --input "60-90:Start;180-240:Start;260-420:Right+B" --reference artifacts\mame\smb3_frame420.bmp --out artifacts\frames\smb3_frame420_nessharp.bmp --diff-out artifacts\frames\smb3_frame420_diff.bmp
.\scripts\compare-mame-frame.ps1 "roms\USA\Super Mario Bros 3 (U) (PRG 1).nes" -Frame 420 -InputScript "60-90:Start;180-240:Start;260-420:Right+B" -ScanRadius 4
.\scripts\compare-mame-frame.ps1 "roms\USA\Super Mario Bros 3 (U) (PRG 1).nes" -Frame 1000 -InputScript "60-90:Start;180-240:Start;420-470:Right;540-590:Up;700-750:A;920-1500:Right+B" -ScanRadius 6
dotnet run --project src\NesSharp.Cli -- scan-frame-match "roms\USA\Super Mario Bros 3 (U) (PRG 1).nes" --reference artifacts\mame\smb3_frame420.bmp --start-frame 360 --end-frame 480
dotnet run --project src\NesSharp.Cli -- sample-frames "roms\USA\Super Mario Bros 3 (U) (PRG 1).nes" --start-frame 300 --end-frame 1200 --step 60 --input "60-90:Start;180-240:Start"
dotnet run --project src\NesSharp.Cli -- diagnose-frame "roms\USA\Super Mario Bros 3 (U) (PRG 1).nes" --frames 300
dotnet run --project src\NesSharp.Desktop -- test-roms\nes-test-roms\ppu_read_buffer\test_ppu_read_buffer.nes
```

Desktop controller 1 mapping: `Z` = A, `X` = B, `Backspace` = Select, `Enter` = Start, arrow keys = D-pad. Use `Ctrl+R` for reset, `Ctrl+Shift+R` for power cycle, Space to pause, `F10` to advance one frame, and `Ctrl+D` to capture the current frame plus PPU/OAM/Mapper 4 diagnostics under `artifacts\desktop-captures`.
Battery-backed saves are loaded from and written to a `.sav` file beside the ROM in the desktop host.

`render-frame` and `compare-frame` support 256x240 binary PPM (`P6`) and uncompressed 24-bit BMP files. `compare-frame` reports the bounding box of changed pixels, `--diff-out` writes a black-background visual diff image where changed channels are amplified for inspection, `--normalize-palette` snaps RGB frames to the nearest nesSharp NES palette color before comparing, `--actual-x-offset/--actual-y-offset` compare with an applied actual-frame offset, and `--offset-radius N` reports the best small x/y alignment offset. The MAME wrapper uses palette-normalized comparison, `-ActualXOffset 2`, and `-OffsetRadius 8` by default; pass `-ExactRgb` for strict screenshot RGB comparison.
Frame rendering commands also support `--input`, using semicolon-separated frame ranges like `60-90:Start;260-420:Right+B`.
For local reference captures, MAME 0.288 can be installed under `tools\mame-0.288\mame.exe`; the `tools` folder is intentionally ignored by git. `scripts\capture-mame-frame.ps1` uses MAME Lua frame callbacks to capture exact reference frames, supports `-InputScript` with the same frame-range syntax as nesSharp, and converts MAME's PNG snapshot to BMP/PPM/PNG output. `scripts\compare-mame-frame.ps1` wraps the full capture/compare loop and writes MAME, nesSharp, and diff BMPs under `artifacts\frame-compare`; use `-ScanRadius N` to scan nearby nesSharp frames against the captured MAME reference.
Use `dotnet run --project src\NesSharp.Cli -- trace-dma <rom.nes>` to inspect OAM/DMC DMA event timing while working on DMA overlap compatibility.

## Current Status

- Parses iNES headers.
- Loads Mapper 0 / NROM cartridges.
- Supports NROM PRG mirroring, CHR ROM, CHR RAM, trainer skipping, and PRG RAM at `$6000-$7FFF`.
- Supports Mapper 1 / MMC1 serial register writes, PRG/CHR bank switching, PRG RAM enable, and switchable nametable mirroring for common SxROM games.
- Supports Mapper 2 / UxROM switchable 16 KB PRG banking, fixed last PRG bank, CHR RAM, and fixed header mirroring.
- Supports Mapper 3 / CNROM fixed PRG with switchable 8 KB CHR banks.
- Supports Mapper 7 / AxROM switchable 32 KB PRG banking, CHR RAM, and mapper-controlled one-screen mirroring.
- Exposes mapper PRG RAM through the cartridge and persists battery-backed save RAM in the desktop host.
- Provides a CPU bus shell with internal RAM mirroring, PPU register mirroring, and cartridge space.
- Provides a CPU reset skeleton that reads the reset vector from cartridge ROM.
- Executes the 6502 official opcode set and the stable unofficial opcodes needed by `nestest`.
- Matches all 8,991 instruction states in `nestest.log` when started at `$C000`.
- Runs blargg-style test ROMs headlessly by reading the `$6000` status/output convention.
- Runs sprite hit/overflow test ROMs that report final results through `$00F8`.
- Passes all 16 NROM `instr_test-v5/rom_singles` CPU instruction ROMs.
- Passes `instr_timing/rom_singles` instruction and branch timing ROMs.
- Passes `branch_timing_tests` branch edge-case timing ROMs.
- Tracks basic NTSC PPU dot/scanline/frame timing.
- Implements PPU vblank set/clear behavior, `$2002` read side effects, basic NMI control, and odd-frame clock skip.
- Advances PPU timing during CPU bus accesses, with raw bus reads preserved for tracing/debug inspection.
- Models the `$2002` vblank-set suppression window and near-vblank NMI cancellation.
- Implements CPU-visible PPU VRAM, nametable mirroring, palette mirroring, buffered `$2007` reads, `$2005/$2006` write latch behavior, OAM, phase-aligned OAM DMA cycle timing, and PPU open-bus decay.
- Implements pragmatic visible-scanline sprite composition, per-scanline sprite OAM/pattern latching, sprite/background priority, sprite flipping, 8x16 sprite tile selection, timed sprite overflow status, diagonal overflow search behavior, and sprite 0 hit detection. All `sprite_hit_tests_2005.10.05` and `sprite_overflow_tests` ROMs currently pass.
- Maintains a 256x240 palette-index framebuffer and can export it as binary PPM or 24-bit BMP through the CLI.
- Provides CLI frame comparison, frame-range scanning, and compact frame sampling for MAME/reference-emulator visual checks.
- Provides a CLI frame diagnostic command that prints PPU timing/scroll/fetch/OAM state and Mapper 4 bank/IRQ state at a target frame.
- Uses a shared MAME-compatible NTSC NES RGB palette for frame export, desktop display, and visual regression hashing.
- Includes a deterministic RGB hash regression for `ppu_read_buffer` frame 120.
- Implements standard controller strobe/read behavior on `$4016/$4017`.
- Adds initial CPU-visible APU register scaffolding for `$4000-$4013`, `$4015`, and `$4017`, including channel enable/status bits and frame interrupt status behavior.
- Supports CPU IRQ servicing and routes APU frame IRQs to the CPU when not inhibited.
- Models initial pulse-channel register state, length halt, envelope restart/clocking, timer period, and sweep target calculation.
- Models initial triangle-channel register state, timer period, length counter, and linear counter reload/control behavior.
- Models initial noise-channel register state, period/mode, length counter, envelope restart/clocking, and shift-register feedback.
- Models initial DMC register state, direct output level, sample address/length, status bit, looping, IRQ flag behavior, and mapper-backed delta sample playback.
- Adds first-pass DMC DMA sample-fetch stalls for load and reload fetches, with fetches deferred until a delayed CPU read cycle can be halted.
- Accounts for DMC DMA dummy/get alignment from the CPU bus DMA phase clock.
- Services pending DMC DMA during OAM DMA transfers in the first-pass DMA arbiter.
- Models first-pass DMC DMA controller read conflicts by consuming an extra `$4016/$4017` read.
- Counts DMC DMA no-op cycles when repeating side-effect reads for `$2002`, `$2007`, and `$4015`.
- Produces a drainable mono APU sample buffer using channel timer clocks and the NES nonlinear mixer formula.
- Includes an initial WinForms desktop host with framebuffer display, audio playback, ROM loading, reset/pause actions, and keyboard input for controller 1.
- Captures desktop diagnostics with a BMP frame dump and text state dump for exact-frame compatibility debugging.
- Runs desktop emulation on a background frame loop with UI-thread framebuffer presentation.
- Includes a desktop power-cycle command that rebuilds mapper/machine state while preserving battery-backed save data.
- Passes all `ppu_vbl_nmi` ROM singles, `01-vbl_basics` through `10-even_odd_timing`.
- Passes all `vbl_nmi_timing` ROMs through the `$00F8` result runner.
- Passes `ppu_open_bus`, `ppu_read_buffer`, `oam_read`, and `oam_stress`.
- Includes focused xUnit coverage using synthetic ROMs and the downloaded `nestest.nes`.

## Known Next Accuracy Work

The next high-value accuracy target is tighter DMC DMA scheduling, especially one-byte sample edge cases and OAM/DMC overlap timing exposed by `sprdma_and_dmc_dma`. Rendering is currently pragmatic rather than cycle-perfect: it is good enough for first framebuffer inspection and focused sprite/background tests, but scrolling during rendering and fetch timing still need refinement.

The staged implementation plan is in `docs/implementation-plan.md`.
