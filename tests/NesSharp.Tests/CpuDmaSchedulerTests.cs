using NesSharp.Core.Memory;

namespace NesSharp.Tests;

public sealed class CpuDmaSchedulerTests
{
    [Theory]
    [InlineData(false, 513)]
    [InlineData(true, 514)]
    public void OamDmaTakesExpectedCyclesAfterCpuWrite(bool firstCycleIsGet, int expectedCycles)
    {
        var schedule = CpuDmaScheduler.Simulate(new CpuDmaScheduleRequest(
            firstCycleIsGet,
            OamByteCount: 256,
            DmcStartCycle: null));

        Assert.Equal(expectedCycles, schedule.CycleCount);
        Assert.Equal(256, schedule.Events.Count(e => e.Kind == CpuDmaCycleKind.OamRead));
        Assert.Equal(256, schedule.Events.Count(e => e.Kind == CpuDmaCycleKind.OamWrite));
    }

    [Theory]
    [InlineData(false, 513)]
    [InlineData(true, 512)]
    public void OamDmaCanStartAfterSharedHaltCycle(bool firstCycleIsGet, int expectedCycles)
    {
        var schedule = CpuDmaScheduler.Simulate(new CpuDmaScheduleRequest(
            firstCycleIsGet,
            OamByteCount: 256,
            DmcStartCycle: null,
            OamHaltAlreadyDone: true));

        Assert.Equal(expectedCycles, schedule.CycleCount);
    }

    [Fact]
    public void DmcReadyAtOamBoundaryCanShareHaltCycle()
    {
        var schedule = CpuDmaScheduler.Simulate(new CpuDmaScheduleRequest(
            FirstCycleIsGet: true,
            OamByteCount: 256,
            DmcStartCycle: 0,
            OamHaltAlreadyDone: true));

        Assert.Equal(2, schedule.Events.Single(e => e.Kind == CpuDmaCycleKind.DmcRead).Cycle);
    }

    [Fact]
    public void DmcAndOamDmaStartingTogetherOnGetCycleRunsDmcBeforeOam()
    {
        var schedule = CpuDmaScheduler.Simulate(new CpuDmaScheduleRequest(
            FirstCycleIsGet: true,
            OamByteCount: 2,
            DmcStartCycle: 0));

        Assert.Equal(
            [
                CpuDmaCycleKind.DummyRead,
                CpuDmaCycleKind.DummyRead,
                CpuDmaCycleKind.DmcRead,
                CpuDmaCycleKind.DummyRead,
                CpuDmaCycleKind.OamRead,
                CpuDmaCycleKind.OamWrite
            ],
            schedule.Events.Take(6).Select(e => e.Kind).ToArray());
    }

    [Fact]
    public void DmcAndOamDmaStartingTogetherOnPutCycleAllowsFirstOamByteBeforeDmcRead()
    {
        var schedule = CpuDmaScheduler.Simulate(new CpuDmaScheduleRequest(
            FirstCycleIsGet: false,
            OamByteCount: 2,
            DmcStartCycle: 0));

        Assert.Equal(
            [
                CpuDmaCycleKind.DummyRead,
                CpuDmaCycleKind.OamRead,
                CpuDmaCycleKind.OamWrite,
                CpuDmaCycleKind.DmcRead,
                CpuDmaCycleKind.DummyRead,
                CpuDmaCycleKind.OamRead,
                CpuDmaCycleKind.OamWrite
            ],
            schedule.Events.Select(e => e.Kind).ToArray());
    }

    [Theory]
    [InlineData(CpuDmaDmcState.NeedHalt, false, 3)]
    [InlineData(CpuDmaDmcState.NeedHalt, true, 2)]
    [InlineData(CpuDmaDmcState.NeedDummy, false, 1)]
    [InlineData(CpuDmaDmcState.NeedDummy, true, 2)]
    [InlineData(CpuDmaDmcState.ReadyToRead, false, 1)]
    [InlineData(CpuDmaDmcState.ReadyToRead, true, 0)]
    public void DmcInitialStateControlsFirstReadCycle(
        CpuDmaDmcState initialState,
        bool firstCycleIsGet,
        int expectedReadCycle)
    {
        var schedule = CpuDmaScheduler.Simulate(new CpuDmaScheduleRequest(
            firstCycleIsGet,
            OamByteCount: 0,
            DmcStartCycle: null,
            DmcInitialState: initialState));

        Assert.Equal(expectedReadCycle, schedule.Events.Single(e => e.Kind == CpuDmaCycleKind.DmcRead).Cycle);
    }

    [Fact]
    public void DmcStartStateAppliesWhenRequestAppearsDuringOamDma()
    {
        var schedule = CpuDmaScheduler.Simulate(new CpuDmaScheduleRequest(
            FirstCycleIsGet: false,
            OamByteCount: 4,
            DmcStartCycle: 4,
            DmcStartState: CpuDmaDmcState.NeedDummy));

        Assert.Equal(5, schedule.Events.Single(e => e.Kind == CpuDmaCycleKind.DmcRead).Cycle);
        Assert.Equal(CpuDmaCycleKind.OamWrite, schedule.Events.Single(e => e.Cycle == 4).Kind);
    }

    [Fact]
    public void DmcDmaInMiddleOfOamDmaStealsReadAndRealignsOam()
    {
        var schedule = CpuDmaScheduler.Simulate(new CpuDmaScheduleRequest(
            FirstCycleIsGet: false,
            OamByteCount: 4,
            DmcStartCycle: 4));

        Assert.Equal(11, schedule.CycleCount);
        Assert.Equal(7, schedule.Events.Single(e => e.Kind == CpuDmaCycleKind.DmcRead).Cycle);
        Assert.Equal(
            [CpuDmaCycleKind.OamWrite, CpuDmaCycleKind.DmcRead, CpuDmaCycleKind.DummyRead, CpuDmaCycleKind.OamRead],
            schedule.Events.Skip(6).Take(4).Select(e => e.Kind).ToArray());
    }

    [Fact]
    public void DmcDmaOnSecondToLastOamPutAddsOneCycle()
    {
        var schedule = CpuDmaScheduler.Simulate(new CpuDmaScheduleRequest(
            FirstCycleIsGet: false,
            OamByteCount: 4,
            DmcStartCycle: 6));

        Assert.Equal(10, schedule.CycleCount);
        Assert.Equal(9, schedule.Events.Single(e => e.Kind == CpuDmaCycleKind.DmcRead).Cycle);
        Assert.Equal(3, schedule.Events.Last(e => e.Kind == CpuDmaCycleKind.OamWrite).OamIndex);
    }

    [Fact]
    public void DmcDmaOnLastOamPutExtendsAfterOamDma()
    {
        var schedule = CpuDmaScheduler.Simulate(new CpuDmaScheduleRequest(
            FirstCycleIsGet: false,
            OamByteCount: 4,
            DmcStartCycle: 8));

        Assert.Equal(12, schedule.CycleCount);
        Assert.Equal(11, schedule.Events.Single(e => e.Kind == CpuDmaCycleKind.DmcRead).Cycle);
        Assert.Equal(
            [CpuDmaCycleKind.OamWrite, CpuDmaCycleKind.DummyRead, CpuDmaCycleKind.DummyRead, CpuDmaCycleKind.DmcRead],
            schedule.Events.Skip(8).Select(e => e.Kind).ToArray());
    }
}
