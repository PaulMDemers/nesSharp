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
- After the phase checkpoint, a shortcut that completed the DMC DMA on the final operand read before `STA $4014` was rejected because it modeled the wrong cycle. The landed version instead defers the ready DMC DMA to the exact `$4014` write cycle.

## Landed phase checkpoint

The emulator now advances the DMA get/put phase during `NesMachine.StepInstruction()`'s remaining non-bus CPU cycles, without making those cycles DMC halt points. This improved the normal ROM's first rows. The `dmc_dma_during_read4/dma_4016_read.nes` regression from the raw phase experiment was fixed by shortening only the initial DMC load halt delay for rate-0 IRQ-enabled DMC starts; the default rate-0 load path still waits for the second following APU cycle.

Normal ROM after the phase checkpoint:

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

`_512` ROM after the phase checkpoint:

```text
00 527
01 528
02 527
03 527
04 528
05 528
06 527
07 527
08 528
09 529
0A 527
0B 528
0C 527
0D 527
0E 527
0F 528
```

Temporary tracing of the raw phase experiment showed one controller conflict still occurred in `dma_4016_read.nes`, but it landed on the fourth synchronized probe instead of the expected third probe. The narrowed rate-0 IRQ load delay moves the synchronization ROM back into alignment while keeping the existing default DMC load-delay unit behavior.

## Next model to try

The remaining sprdma drift is now concentrated after the normal ROM's first three rows and in the `_512` early rows. Broad one-byte implicit-stop modeling was tested and moved the row table away from the reference, so it should not be retried without a narrower state-level trigger. The next useful probe is around reload DMA timing during the `end_dmc_timer` fine-sync loop, especially why the normal ROM settles at `527` instead of alternating down to `525/526`.

The mirrored source for `sprdma_and_dmc_dma.s` in koute's `pinky` repository identifies the tiny measured routine as:

```asm
test: lda #$07
      sta $4014
      sta $100
      rts
```

Its `dma_timing.inc` header says the test is interested in the case where a memory write occurs during a DMC DMA access, making that DMC DMA take 3 clocks instead of the usual 4. The emulator now carries one narrow write-overlap case: an already-ready DMC DMA seen on the final operand read before an exact absolute write to `$4014` is deferred into the `$4014` write and completed with the shorter write-overlap path.

Normal ROM after the exact `$4014` write-overlap checkpoint:

```text
00 527
01 528
02 527
03 529
04 527
05 527
06 527
07 527
08 527
09 528
0A 527
0B 527
0C 527
0D 527
0E 527
0F 527
```

`_512` is unchanged from the phase checkpoint. The exact deferral fires once in the normal ROM and does not fire in `_512`. This is still not the complete model; a future implementation probably needs an explicit multi-cycle DMC DMA state machine rather than the current mostly all-at-once shortcut, because other write-overlap and alignment cases still cannot be represented cleanly.

## DMA trace checkpoint

`NesSharp.Cli trace-dma` records the current instruction PC, approximate CPU cycle, OAM DMA start/end, ordinary DMC reads, write-overlap DMC reads, and DMC reads that occur during OAM DMA. Passing `--include-status` also includes `$4015` status reads and writes so the DMC sync loops can be correlated with the DMA events. This is intended to keep future DMA work tied to event positions rather than only the final pass/fail table.

Current OAM/DMC overlap summaries:

```text
sprdma_and_dmc_dma.nes
row start next dmc-at-oam-index/access oam-end-access
00  put   -                 518
01  put   -                 518
02  put   -                 518
03  get   -                 523
04  put   -                 521
05  get   -                 521
06  get   0/8               521
07  get   0/8               521
08  get   0/8               521
09  get   0/8               521
0A  get   1/10              521
0B  get   1/10              521
0C  get   2/12              521
0D  get   2/12              521
0E  get   3/14              521
0F  get   3/14              521

sprdma_and_dmc_dma_512.nes
row start next dmc-at-oam-index/access oam-end-access
00  get   252/512           521
01  get   252/512           521
02  get   253/514           521
03  get   253/514           521
04  get   253/514           521
05  get   254/516           521
06  get   255/518           521
07  get   255/518           521
08  get   255/518           521
09  get   -                 523
0A  get   -                 519
0B  get   -                 519
0C  get   -                 519
0D  get   -                 519
0E  get   -                 519
0F  get   -                 519
```

A probe that removed the post-DMC OAM realignment cycle improved some normal-ROM collision rows but overshot other rows and made `_512` less consistent. This supports the earlier conclusion that the fix should be a stateful DMC/OAM DMA scheduler that can represent halt, dummy, alignment, DMC get, and OAM retry phases independently.

A broader probe that skipped the reload-DMA alignment cycle globally made both `sprdma` ROMs time out before printing row 00. Reload fetches still need their normal alignment behavior in the sync loops; the remaining fix must be narrower than "all reload DMA is one cycle shorter."

## First OAM read steal checkpoint

Reload DMC DMA at OAM index 0 now steals the first OAM read slot without adding the usual DMC/OAM retry pair. This moves the normal ROM's first DMC-inside-OAM rows from `527/527/527/528` to `525/525/525/525`:

```text
sprdma_and_dmc_dma.nes
00 527
01 528
02 527
03 529
04 527
05 527
06 525
07 525
08 525
09 525
0A 526
0B 527
0C 527
0D 527
0E 527
0F 527
```

This is closer but still not complete: rows 07 and 09 are one cycle short, rows 0A-0F still drift high, and `_512` is unchanged. The remaining work is likely the same fine-sync/status-poll alignment problem rather than another broad OAM overlap rule.
