# Compatibility Dashboard

Generated: 2026-06-29 23:07:51 -04:00

- Branch: `main`
- Commit: `5273993`
- Configuration: `Release`
- Slow checks: `False`

## Headline Scores

| Area | Current | Notes |
| --- | ---: | --- |
| sprdma_and_dmc_dma | abs=6, max=1 | Normal one-byte sample/OAM overlap table. |
| sprdma_and_dmc_dma_512 | abs=11, max=2 | Late OAM-window DMC reload edge remains the main known gap. |

## Retail Visual Baselines

| Case | Frame/Input | Current | Artifacts | Notes |
| --- | --- | ---: | --- | --- |
| Super Mario Bros. 3 overworld smoke | frame 420, `60-90:Start;180-240:Start;260-420:Right+B` | 0 / 61440 differing pixels | `artifacts\frame-compare\Super_Mario_Bros_3_U_PRG_1_-frame420-*` | Zero-offset match after background shifter phase correction. |
| Super Mario Bros. 3 level 1-1 smoke | frame 1000, `60-90:Start;180-240:Start;420-470:Right;540-590:Up;700-750:A;920-1500:Right+B` | 16 / 61440 differing pixels | `artifacts\frame-compare\Super_Mario_Bros_3_U_PRG_1_-frame1000-*` | Tiny edge-only residual; gameplay field and HUD are structurally aligned. |
| Super Mario Bros. 3 later overworld route | frame 2200, `60-90:Start;180-240:Start;420-470:Right;540-590:Up;700-750:A;920-2200:Right+B` | 0 / 61440 differing pixels | `artifacts\frame-compare\Super_Mario_Bros_3_U_PRG_1_-frame2200-*` | Zero-offset match after background shifter phase correction. |

## Command Results

| Check | Status | Time | Command |
| --- | --- | ---: | --- |
| Release build | pass | 2.2s | `dotnet build src\NesSharp.Cli\NesSharp.Cli.csproj -c Release` |
| DMA/APU focused tests | pass | 8.7s | `dotnet test tests\NesSharp.Tests\NesSharp.Tests.csproj --filter ApuBusTests\|CpuBusTests\|OamDmcDmaTimingTests\|DmcDmaDuringReadRomTests\|SprDmaOutputParserExtractsRowsAndScoresDiffs --logger console;verbosity=minimal` |
| sprdma normal | pass | 14.4s | `dotnet run -c Release --no-build --project src\NesSharp.Cli -- sprdma-report test-roms\nes-test-roms\sprdma_and_dmc_dma\sprdma_and_dmc_dma.nes` |
| sprdma 512 | pass | 12.8s | `dotnet run -c Release --no-build --project src\NesSharp.Cli -- sprdma-report test-roms\nes-test-roms\sprdma_and_dmc_dma\sprdma_and_dmc_dma_512.nes` |

## Next Debugging Targets

- Use `scripts\compare-mame-frame.ps1` for retail visual captures and diffs.
- Continue narrowing `sprdma_and_dmc_dma_512` rows with immediate post-OAM DMC reloads.
- Add any newly failing retail smoke cases here with frame number, input script, and artifact paths.

