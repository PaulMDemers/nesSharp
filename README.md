# nesSharp

A C# NES emulator project, currently in the first implementation milestone: cartridge loading, Mapper 0, CPU/bus scaffolding, and a headless test foundation.

## Current Commands

```powershell
dotnet test NesSharp.slnx
dotnet build NesSharp.slnx
dotnet run --project src\NesSharp.Cli -- info test-roms\nes-test-roms\other\nestest.nes
dotnet run --project src\NesSharp.Cli -- nestest test-roms\nes-test-roms\other\nestest.nes test-roms\nes-test-roms\other\nestest.log
dotnet run --project src\NesSharp.Cli -- test-rom test-roms\nes-test-roms\instr_test-v5\rom_singles\01-basics.nes
dotnet run --project src\NesSharp.Cli -- test-rom test-roms\nes-test-roms\ppu_vbl_nmi\rom_singles\01-vbl_basics.nes
dotnet run --project src\NesSharp.Cli -- render-frame test-roms\nes-test-roms\ppu_read_buffer\test_ppu_read_buffer.nes --frames 120 --out artifacts\frames\ppu_read_buffer_frame120.ppm
dotnet run --project src\NesSharp.Desktop -- test-roms\nes-test-roms\ppu_read_buffer\test_ppu_read_buffer.nes
```

Desktop controller 1 mapping: `Z` = A, `X` = B, `Backspace` = Select, `Enter` = Start, arrow keys = D-pad.

## Current Status

- Parses iNES headers.
- Loads Mapper 0 / NROM cartridges.
- Supports NROM PRG mirroring, CHR ROM, CHR RAM, trainer skipping, and PRG RAM at `$6000-$7FFF`.
- Supports Mapper 3 / CNROM fixed PRG with switchable 8 KB CHR banks.
- Provides a CPU bus shell with internal RAM mirroring, PPU register mirroring, and cartridge space.
- Provides a CPU reset skeleton that reads the reset vector from cartridge ROM.
- Executes the 6502 official opcode set and the stable unofficial opcodes needed by `nestest`.
- Matches all 8,991 instruction states in `nestest.log` when started at `$C000`.
- Runs blargg-style test ROMs headlessly by reading the `$6000` status/output convention.
- Passes all 16 NROM `instr_test-v5/rom_singles` CPU instruction ROMs.
- Tracks basic NTSC PPU dot/scanline/frame timing.
- Implements PPU vblank set/clear behavior, `$2002` read side effects, basic NMI control, and odd-frame clock skip.
- Advances PPU timing during CPU bus accesses, with raw bus reads preserved for tracing/debug inspection.
- Models the `$2002` vblank-set suppression window and near-vblank NMI cancellation.
- Implements CPU-visible PPU VRAM, nametable mirroring, palette mirroring, buffered `$2007` reads, `$2005/$2006` write latch behavior, OAM, OAM DMA, and PPU open-bus decay.
- Implements rudimentary visible-scanline background/sprite 0 pixel overlap detection for `$2002.6` sprite 0 hit.
- Maintains a 256x240 palette-index framebuffer and can export it as binary PPM through the CLI.
- Uses a shared NES RGB palette for frame export and visual regression hashing.
- Includes a deterministic RGB hash regression for `ppu_read_buffer` frame 120.
- Implements standard controller strobe/read behavior on `$4016/$4017`.
- Includes an initial WinForms desktop host with framebuffer display, ROM loading, reset/pause actions, and keyboard input for controller 1.
- Passes `ppu_vbl_nmi` ROMs `01-vbl_basics`, `02-vbl_set_time`, `03-vbl_clear_time`, `04-nmi_control`, `06-suppression`, and `09-even_odd_frames`.
- Passes `ppu_open_bus`, `ppu_read_buffer`, `oam_read`, and `oam_stress`.
- Includes focused xUnit coverage using synthetic ROMs and the downloaded `nestest.nes`.

## Known Next Accuracy Work

The remaining `ppu_vbl_nmi` timing ROMs need tighter CPU/PPU phase modeling around NMI recognition, pre-render clear timing, and rendering-enable odd-frame skip timing. Rendering is currently pragmatic rather than cycle-perfect: it is good enough for first framebuffer inspection and sprite 0 hit tests, but sprite evaluation, scrolling during rendering, and pixel priority still need refinement.

The staged implementation plan is in `docs/implementation-plan.md`.
