namespace NesSharp.Core.Memory;

public static class DmcDmaCpuSequencer
{
    private static readonly DmcDmaCpuPhase[] GetAlignedPhases =
    [
        DmcDmaCpuPhase.Halt,
        DmcDmaCpuPhase.Dummy,
        DmcDmaCpuPhase.Get
    ];

    private static readonly DmcDmaCpuPhase[] PutAlignedPhases =
    [
        DmcDmaCpuPhase.Halt,
        DmcDmaCpuPhase.Dummy,
        DmcDmaCpuPhase.Alignment,
        DmcDmaCpuPhase.Get
    ];

    public static ReadOnlySpan<DmcDmaCpuPhase> StandaloneFetch(bool nextDmaCycleIsGet)
    {
        return nextDmaCycleIsGet
            ? GetAlignedPhases
            : PutAlignedPhases;
    }
}

public enum DmcDmaCpuPhase
{
    Halt,
    Dummy,
    Alignment,
    Get
}
