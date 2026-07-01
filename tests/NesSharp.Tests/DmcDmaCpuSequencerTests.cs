using NesSharp.Core.Memory;

namespace NesSharp.Tests;

public sealed class DmcDmaCpuSequencerTests
{
    [Theory]
    [InlineData(true, new[] { DmcDmaCpuPhase.Halt, DmcDmaCpuPhase.Dummy, DmcDmaCpuPhase.Get })]
    [InlineData(false, new[] { DmcDmaCpuPhase.Halt, DmcDmaCpuPhase.Dummy, DmcDmaCpuPhase.Alignment, DmcDmaCpuPhase.Get })]
    public void StandaloneFetchBuildsExplicitCpuDmaPhases(bool nextDmaCycleIsGet, DmcDmaCpuPhase[] expected)
    {
        Assert.Equal(expected, DmcDmaCpuSequencer.StandaloneFetch(nextDmaCycleIsGet).ToArray());
    }
}
