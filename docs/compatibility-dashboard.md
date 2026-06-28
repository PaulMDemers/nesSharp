# Compatibility Dashboard

Generated: 2026-06-28 16:54:32 -04:00

- Branch: `main`
- Commit: `09e80f1`
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
| Super Mario Bros. 3 overworld smoke | frame 420, `60-90:Start;180-240:Start;260-420:Right+B` | 1519 / 60960 differing pixels after `dx=2` | `artifacts\frame-compare\Super_Mario_Bros_3_U_PRG_1_-frame420-*` | Residual diff is mostly animated overworld star markers and thin edge artifacts. |

## Command Results

| Check | Status | Time | Command |
| --- | --- | ---: | --- |
| DMA/APU focused tests | pass | 11.1s | `dotnet test tests\NesSharp.Tests\NesSharp.Tests.csproj --filter ApuBusTests\|CpuBusTests\|OamDmcDmaTimingTests\|DmcDmaDuringReadRomTests\|SprDmaOutputParserExtractsRowsAndScoresDiffs --logger console;verbosity=minimal` |
| sprdma normal | pass | 16.3s | `dotnet run -c Release --no-build --project src\NesSharp.Cli -- sprdma-report test-roms\nes-test-roms\sprdma_and_dmc_dma\sprdma_and_dmc_dma.nes` |
| sprdma 512 | pass | 13.8s | `dotnet run -c Release --no-build --project src\NesSharp.Cli -- sprdma-report test-roms\nes-test-roms\sprdma_and_dmc_dma\sprdma_and_dmc_dma_512.nes` |

## Next Debugging Targets

- Use `scripts\compare-mame-frame.ps1` for retail visual captures and diffs.
- Continue narrowing `sprdma_and_dmc_dma_512` rows with immediate post-OAM DMC reloads.
- Add any newly failing retail smoke cases here with frame number, input script, and artifact paths.

