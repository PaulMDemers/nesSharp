# nesSharp Implementation Plan

This plan targets a test-driven NES emulator in modern C#. The early goal is a headless core that can pass CPU and memory-reported test ROMs before we spend effort on UI, audio polish, or retail game compatibility.

## Reference Baseline

- NESdev Emulator tests: https://www.nesdev.org/wiki/Emulator_tests
- NESdev CPU memory map: https://www.nesdev.org/wiki/CPU_memory_map
- NESdev iNES / NES 2.0 header notes: https://www.nesdev.org/wiki/INES
- NESdev PPU rendering timing: https://www.nesdev.org/wiki/PPU_rendering
- NESdev PPU programmer reference: https://www.nesdev.org/wiki/PPU_programmer_reference
- NESdev CPU unofficial opcodes: https://www.nesdev.org/wiki/CPU_unofficial_opcodes
- NESdev mapper overview: https://www.nesdev.org/wiki/Mapper
- Public test ROM archive cloned locally: `test-roms/nes-test-roms`

## Project Shape

Recommended solution layout:

- `src/NesSharp.Core`: no UI dependencies. Cartridge parsing, bus, CPU, PPU, APU, controllers, mappers, save RAM, and emulation loop.
- `src/NesSharp.Cli`: command-line runner for traces, headless ROM tests, and basic debugging.
- `src/NesSharp.Desktop`: eventual desktop frontend for video, audio, input, pause/debugger controls, and ROM loading.
- `tests/NesSharp.Tests`: unit tests for CPU, cartridge loading, mapper behavior, PPU register semantics, APU counters, and harness fixtures.
- `docs`: implementation notes, hardware quirks, and compatibility/test status.

Keep the core deterministic and host-independent. The frontend should only provide input, receive video/audio buffers, and call the emulator clock/frame API.

## Milestone 0: Tooling And Harness

1. Create the .NET solution and projects.
2. Add a headless test harness that can:
   - Load `.nes` files.
   - Run a fixed number of CPU cycles or frames.
   - Read CPU RAM / cartridge RAM ranges for test output.
   - Emit CPU traces in `nestest.log`-comparable format.
3. Add CI-style commands:
   - `dotnet test`
   - `dotnet run --project src/NesSharp.Cli -- test-rom <path>`
   - `dotnet run --project src/NesSharp.Cli -- trace <path> --start C000`

Deliverable: a blank emulator shell that can parse ROMs and fail tests in informative ways.

## Milestone 1: Cartridge And Mapper 0

Implement iNES header parsing first:

- Validate the `NES<EOF>` signature.
- Extract PRG ROM size, CHR ROM size, trainer presence, mirroring, battery PRG RAM, mapper number, and NES 2.0 hint bits.
- Support trainer skipping.
- Support CHR RAM when CHR ROM size is zero.
- Implement mapper 0 / NROM:
  - 16 KB PRG mirrored at `$C000-$FFFF` when only one PRG bank exists.
  - 32 KB PRG mapped at `$8000-$FFFF` when two banks exist.
  - CHR ROM/RAM mapped through PPU `$0000-$1FFF`.

Deliverable: `nestest.nes` and simple NROM test ROMs load with a correct memory map.

## Milestone 2: CPU Correctness

Implement the Ricoh 2A03 CPU as a 6502 variant:

- Registers: A, X, Y, P, SP, PC.
- Status flags with NES-specific reset behavior and no usable decimal mode.
- All official opcodes and addressing modes.
- Exact per-instruction cycle counts, including branch and page-crossing penalties.
- Interrupts: NMI, IRQ, BRK, reset, delayed IRQ inhibition behavior.
- DMA-visible cycle behavior enough for OAM DMA and later DMC DMA.
- Unofficial opcodes after official opcode tests pass, because some games and later tests need them.

First tests:

- `test-roms/nes-test-roms/other/nestest.nes`
  - Start at `$C000` for automation and compare against `nestest.log`.
  - Success code is written into CPU RAM locations `$0002-$0003` in automation mode.
- `test-roms/nes-test-roms/blargg_nes_cpu_test5/official.nes`
- `test-roms/nes-test-roms/instr_test-v5/official_only.nes`
- `test-roms/nes-test-roms/branch_timing_tests/*.nes`

Deliverable: official CPU instructions pass trace comparison and memory-reported CPU tests.

## Milestone 3: Bus, RAM, PPU Registers, Controllers

Implement enough system integration for CPU-driven tests:

- CPU memory map:
  - `$0000-$07FF`: 2 KB internal RAM.
  - `$0800-$1FFF`: RAM mirrors.
  - `$2000-$2007`: PPU registers.
  - `$2008-$3FFF`: PPU register mirrors.
  - `$4000-$4017`: APU and I/O registers.
  - `$4014`: OAM DMA.
  - `$4016-$4017`: controller shift registers.
  - `$4020-$FFFF`: cartridge space.
- PPU register semantics:
  - `PPUCTRL`, `PPUMASK`, `PPUSTATUS`, `OAMADDR`, `OAMDATA`, `PPUSCROLL`, `PPUADDR`, `PPUDATA`.
  - VRAM address latch behavior.
  - PPU read buffer behavior.
  - Palette mirroring basics.
- Controller strobe/read behavior for standard pads.

First tests:

- `cpu_dummy_reads`
- `cpu_dummy_writes`
- `cpu_exec_space`
- `read_joy3` after controller basics exist.

Deliverable: CPU tests that depend on memory-mapped I/O begin to pass.

## Milestone 4: PPU Frame And Rendering

Build a cycle-based NTSC PPU:

- 262 scanlines per frame, 341 PPU dots per scanline.
- 3 PPU cycles per CPU cycle.
- Visible scanlines 0-239, post-render scanline 240, vblank 241-260, pre-render scanline 261.
- VBlank flag and NMI timing.
- Background tile fetching, nametable/attribute decoding, scrolling registers, fine X.
- Sprite evaluation, OAM, sprite pattern fetch, sprite 0 hit, sprite overflow behavior.
- Palette output to a 256x240 framebuffer.

First tests:

- `blargg_ppu_tests_2005.09.15b/*.nes`
- `ppu_read_buffer`
- `ppu_vbl_nmi`
- `vbl_nmi_timing`
- `sprite_hit_tests_2005.10.05`
- `sprite_overflow_tests`

Deliverable: NROM homebrew/test ROMs render stable frames and PPU tests report pass or produce expected screenshots.

## Milestone 5: APU And Audio

Start with CPU-visible APU behavior, then audio output:

- Frame counter timing. Initial CPU-visible `$4017` frame counter mode/inhibit state and frame interrupt status bit implemented.
- Pulse, triangle, noise, DMC channel state. Initial pulse-channel register/timer state implemented.
- Length counters, envelopes, sweeps, linear counter. Pulse length halt, envelope restart/clocking, and sweep target calculation implemented.
- `$4015` status behavior. Initial channel enable, length-counter status, and status-read frame interrupt clearing implemented.
- Frame IRQ and DMC IRQ behavior. CPU IRQ servicing and APU frame IRQ delivery implemented; DMC IRQ remains pending.
- DMC DMA timing/corruption quirks after basic DMC works.
- Mixer and sample generation for the desktop host.

First tests:

- `apu_test/rom_singles/*.nes`
- `apu_reset/*.nes`
- `dmc_tests/*.nes`
- `sprdma_and_dmc_dma`
- `dmc_dma_during_read4`

Deliverable: CPU-visible APU tests pass, and audio playback is good enough for common NROM games.

## Milestone 6: Common Mappers

After NROM is stable, add mappers by compatibility payoff:

1. Mapper 2 / UNROM / UOROM. Implemented.
2. Mapper 3 / CNROM. Implemented.
3. Mapper 1 / MMC1. Initial common SxROM behavior implemented.
4. Mapper 7 / AxROM. Implemented.
5. Mapper 4 / MMC3.

Each mapper should be isolated behind an `IMapper` interface with CPU read/write, PPU read/write, mirroring, IRQ, and save-state hooks.

First tests:

- `nrom368`
- `mmc3_irq_tests`
- `mmc3_test`
- `mmc3_test_2`
- Mapper-specific tests listed on NESdev once the relevant mapper exists.

Deliverable: broad commercial compatibility begins, but only after test ROM coverage confirms each mapper.

## Milestone 7: Frontend, Debugger, And Save State

Frontend features:

- ROM picker.
- Video scaling with integer scale options and aspect correction.
- Keyboard/controller input mapping.
- Audio device selection and latency controls.
- Pause, reset, power cycle.
- Save RAM persistence. Implemented for battery-backed cartridges in the desktop host.
- Save states.

Debugger features:

- CPU registers, flags, disassembly, stepping.
- Memory viewer.
- PPU nametable, pattern table, palette, OAM viewers.
- Trace capture and comparison tools.

Deliverable: usable emulator app and a debugging surface that helps future accuracy work.

## Automation Strategy

Use three test layers:

1. Unit tests for pure logic:
   - Opcode decode/addressing.
   - Header parsing.
   - Mapper banking.
   - PPU register latch/read-buffer behavior.
2. Headless ROM tests:
   - `nestest` trace comparison.
   - Blargg-style `$6000` output parsing.
   - Fixed-cycle pass/fail watchdogs.
3. Visual/audio regression:
   - Deterministic input scripts.
   - Frame hashes for known screenshots.
   - Audio buffer hashes for focused APU tests where practical.

Blargg-style memory output convention:

- Text begins at `$6004`.
- `$6000` is status: `$80` running, `$81` reset requested, `$00-$7F` completed result code.
- `$6001-$6003` contains the signature bytes `$DE $B0 $61`.
- Result code `0` usually means passed for these memory-output tests.

NESdev specifically recommends automated emulator test suites. We should make the CLI harness a first-class feature rather than an afterthought.

## Initial Definition Of Done

The first "real" success checkpoint:

- ROM loader supports iNES NROM.
- CPU official opcodes pass `nestest` trace comparison from `$C000`.
- `nestest` automation mode reports success in RAM.
- `official.nes` and `official_only.nes` pass.
- The CLI can run these in one command and return nonzero on failure.

That gives us a stable core to build PPU and APU work on top of.
