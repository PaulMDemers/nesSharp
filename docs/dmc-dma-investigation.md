# DMC DMA Timing Investigation

## Current failing ROM

`test-roms/nes-test-roms/sprdma_and_dmc_dma/sprdma_and_dmc_dma.nes` and `_512.nes` both run to completion, but the measured cycle table still differs from the reference checksum.

Reference values from AprNes for `sprdma_and_dmc_dma.nes`:

```text
00 527
01 528
02 527
03 528
04 527
05 526
06 525
07 526
08 525
09 526
0A 525
0B 526
0C 525
0D 526
0E 525
0F 526
```

Current values at commit `006c28a`:

```text
00 527
01 529
02 529
03 527
04 526
05 527
06 529
07 527
08 527
09 525
0A 527
0B 526
0C 526
0D 526
0E 527
0F 527
```

Reference values for `_512.nes`:

```text
00 525
01 526
02 525
03 526
04 524
05 525
06 526
07 527
08 527
09 528
0A 526
0B 527
0C 527
0D 528
0E 527
0F 528
```

Current values at commit `006c28a`:

```text
00 527
01 527
02 526
03 525
04 526
05 527
06 527
07 527
08 528
09 528
0A 527
0B 528
0C 528
0D 528
0E 527
0F 528
```

## Trace findings

Temporary tracing showed that the normal ROM's row drift is dominated by the DMC timer loop, not by the OAM copy itself.

Important PCs in `end_dmc_timer`:

```text
E280: A9 1F       lda #$1F
E282: 8D 15 40    sta $4015
E285: EA          nop
E286: 8D 15 40    sta $4015
E291: 2C 15 40    bit $4015       ; coarse sync poll
E29A: 8D 15 40    sta $4015       ; fine sync sample restart
E2A3-E2A8         fine sync delay/sbc/bne loop
E2AD: 2C 15 40    bit $4015       ; fine sync poll
```

Normal ROM row summary from the trace:

```text
row actual exp diff oam oam-dmc load-dma fine-runs fine-defers
00  527 527   0   514    0       4        17        9
01  529 528  +1   514    0       4         3        2
02  529 527  +2   515    0       3         3        2
03  527 528  -1   515    0       3        17        9
04  526 527  -1   514    0       3        16        8
05  527 526  +1   514    0       3        17        9
06  529 525  +4   515    0       3         3        2
07  527 526  +1   517    1       4        17        9
08  527 525  +2   516    1       4        17        9
09  525 526  -1   516    1       3        15        8
0A  527 525  +2   517    1       3        17        9
0B  526 526   0   516    1       3        16        8
0C  526 525  +1   517    1       3        16        8
0D  526 526   0   517    1       3        16        8
0E  527 525  +2   517    1       4        17        9
0F  527 526  +1   517    1       4        17        9
```

`fine-runs` and `fine-defers` count reload DMC activity around `E2A3-E2AD`. Rows with the same OAM cycle count still diverge, so the next fix should focus on DMC reload scheduling/status behavior inside the one-byte DMC timer loop.

## Rejected experiments

- Advancing DMA phase through all synthetic CPU cycles made the row pattern more regular but caused focused tests to hang.
- Making reload fetches ready immediately, delaying reload fetches longer, and applying universal put-phase reload deferral all moved rows but overshot early rows.
- Reducing `$4015` repeated side-effect reads by one had no effect on either ROM.
- A synthetic OAM second-to-last-put edge test was too broad and worsened `_512`, so it was not kept.
- Moving `Dmc.ClockDmaDelay()` after `ClockChannelTimers()` had the same mixed pattern as immediate reload readiness and was reverted.

## Later phase experiment

An experiment advanced the DMA get/put phase during `NesMachine.StepInstruction()`'s remaining non-bus CPU cycles, without making those cycles DMC halt points. This improved the normal ROM's first rows, but regressed `dmc_dma_during_read4/dma_4016_read.nes`.

Normal ROM with this phase advance:

```text
00 527
01 528
02 527
03 529
04 527
05 529
06 527
07 527
08 527
09 527
0A 527
0B 527
0C 527
0D 527
0E 527
0F 527
```

The focused DMA/APU slice then failed only `dma_4016_read.nes`. Temporary tracing showed one controller conflict still occurred, so the regression is likely that the conflict landed on the wrong one of the five synchronized reads, not that the bus consumed the wrong number of controller bits. Shortening the integrated DMC load halt delay made `dma_4016_read.nes` pass in one probe but worsened both `sprdma` tables, so the phase fix needs a more precise load scheduling model before it can land.

## Next model to try

The remaining behavior likely needs an explicit CPU-cycle DMA phase model plus more precise DMC load scheduling. Broad one-byte implicit-stop modeling was tested and moved the row table away from the reference, so it should not be retried without a narrower state-level trigger.
