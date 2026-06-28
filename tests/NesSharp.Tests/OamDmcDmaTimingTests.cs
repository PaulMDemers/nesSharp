using NesSharp.Core.Apu;
using NesSharp.Core.Memory;

namespace NesSharp.Tests;

public sealed class OamDmcDmaTimingTests
{
    [Theory]
    [InlineData(0, DmcDmaKind.Reload, true)]
    [InlineData(0, DmcDmaKind.Load, false)]
    [InlineData(1, DmcDmaKind.Reload, false)]
    public void OnlyFirstOamReloadCanStealReadWithoutRetry(int oamIndex, DmcDmaKind dmcKind, bool expected)
    {
        var plan = OamDmcDmaTiming.Plan(oamIndex, dmcKind, oamDmaStartedWithDmcReady: true);

        Assert.Equal(expected, plan.StealsFirstOamReadWithoutRetry);
    }

    [Theory]
    [InlineData(0, DmcDmaKind.Reload, false)]
    [InlineData(0, DmcDmaKind.Load, true)]
    [InlineData(1, DmcDmaKind.Reload, true)]
    [InlineData(253, DmcDmaKind.Reload, true)]
    public void OnlyFirstOamReloadRunsBeforeTheOamByte(int oamIndex, DmcDmaKind dmcKind, bool expected)
    {
        var plan = OamDmcDmaTiming.Plan(oamIndex, dmcKind, oamDmaStartedWithDmcReady: true);

        Assert.Equal(expected, plan.RunsAfterOamByte);
    }

    [Theory]
    [InlineData(true, "dmc-during-oam-start-ready")]
    [InlineData(false, "dmc-during-oam-setup-ready")]
    public void FirstOamReloadLabelsHowItBecameReady(bool startedReady, string expectedKind)
    {
        var plan = OamDmcDmaTiming.Plan(0, DmcDmaKind.Reload, startedReady);

        Assert.Equal(expectedKind, plan.ObservationKind);
    }

    [Theory]
    [InlineData(1, DmcDmaKind.Reload, true)]
    [InlineData(2, DmcDmaKind.Reload, true)]
    [InlineData(3, DmcDmaKind.Reload, true)]
    [InlineData(4, DmcDmaKind.Reload, false)]
    [InlineData(251, DmcDmaKind.Reload, false)]
    [InlineData(252, DmcDmaKind.Reload, true)]
    [InlineData(253, DmcDmaKind.Reload, true)]
    [InlineData(254, DmcDmaKind.Reload, true)]
    [InlineData(255, DmcDmaKind.Reload, false)]
    [InlineData(2, DmcDmaKind.Load, false)]
    [InlineData(253, DmcDmaKind.Load, false)]
    public void ReloadOnlySkipsFinalRealignmentOnKnownOamEdges(int oamIndex, DmcDmaKind dmcKind, bool expected)
    {
        Assert.Equal(expected, OamDmcDmaTiming.ShouldSkipFinalRealignment(oamIndex, dmcKind));
    }
}
