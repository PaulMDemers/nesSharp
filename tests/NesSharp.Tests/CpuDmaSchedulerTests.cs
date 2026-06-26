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
