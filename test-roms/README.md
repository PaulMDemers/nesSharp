# Test ROMs

This folder keeps public emulator test ROMs separate from the existing `roms` collection. The retail/pirate/translated ROM set should be saved for later compatibility checks, not early correctness work.

## Downloaded Archive

- Source: https://github.com/christopherpow/nes-test-roms
- Local path: `test-roms/nes-test-roms`
- Cloned commit: `95d8f62`
- Current local `.nes` count: 290

## First Fixtures To Use

- `nes-test-roms/other/nestest.nes`
- `nes-test-roms/other/nestest.log`
- `nes-test-roms/blargg_nes_cpu_test5/official.nes`
- `nes-test-roms/instr_test-v5/official_only.nes`
- `nes-test-roms/branch_timing_tests/1.Branch_Basics.nes`
- `nes-test-roms/branch_timing_tests/2.Backward_Branch.nes`
- `nes-test-roms/branch_timing_tests/3.Forward_Branch.nes`

## Notes

Many blargg-style tests can be checked without a working PPU by reading their output from cartridge RAM around `$6000`. The harness should support this early.

