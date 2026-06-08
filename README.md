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
```

## Current Status

- Parses iNES headers.
- Loads Mapper 0 / NROM cartridges.
- Supports NROM PRG mirroring, CHR ROM, CHR RAM, trainer skipping, and PRG RAM at `$6000-$7FFF`.
- Provides a CPU bus shell with internal RAM mirroring, PPU register mirroring, and cartridge space.
- Provides a CPU reset skeleton that reads the reset vector from cartridge ROM.
- Executes the 6502 official opcode set and the stable unofficial opcodes needed by `nestest`.
- Matches all 8,991 instruction states in `nestest.log` when started at `$C000`.
- Runs blargg-style test ROMs headlessly by reading the `$6000` status/output convention.
- Passes all 16 NROM `instr_test-v5/rom_singles` CPU instruction ROMs.
- Tracks basic NTSC PPU dot/scanline/frame timing.
- Implements PPU vblank set/clear behavior, `$2002` read side effects, basic NMI control, and odd-frame clock skip.
- Passes `ppu_vbl_nmi` ROMs `01-vbl_basics`, `03-vbl_clear_time`, `04-nmi_control`, and `09-even_odd_frames`.
- Includes focused xUnit coverage using synthetic ROMs and the downloaded `nestest.nes`.

## Known Next Accuracy Work

The remaining `ppu_vbl_nmi` timing ROMs need CPU/PPU synchronization at individual CPU memory-access boundaries, not only after each instruction. That is the next accuracy step before the stricter vblank/NMI suppression and one-dot NMI timing tests can pass.

The staged implementation plan is in `docs/implementation-plan.md`.
