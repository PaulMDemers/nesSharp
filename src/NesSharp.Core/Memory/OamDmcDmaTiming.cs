using NesSharp.Core.Apu;

namespace NesSharp.Core.Memory;

public static class OamDmcDmaTiming
{
    public static OamDmcDmaServicePlan Plan(int oamIndex, DmcDmaKind dmcKind, bool oamDmaStartedWithDmcReady)
    {
        var stealsFirstOamReadWithoutRetry = oamIndex == 0 && dmcKind == DmcDmaKind.Reload;
        var runsAfterOamByte = !stealsFirstOamReadWithoutRetry;
        var observationKind = stealsFirstOamReadWithoutRetry
            ? oamDmaStartedWithDmcReady ? "dmc-during-oam-start-ready" : "dmc-during-oam-setup-ready"
            : "dmc-during-oam";

        return new OamDmcDmaServicePlan(
            stealsFirstOamReadWithoutRetry,
            runsAfterOamByte,
            ShouldSkipFinalRealignment(oamIndex, dmcKind),
            observationKind);
    }

    public static bool ShouldSkipFinalRealignment(int oamIndex, DmcDmaKind dmcKind)
    {
        if (dmcKind != DmcDmaKind.Reload)
        {
            return false;
        }

        return oamIndex is >= 2 and <= 3 or >= 252 and <= 254;
    }
}

public readonly record struct OamDmcDmaServicePlan(
    bool StealsFirstOamReadWithoutRetry,
    bool RunsAfterOamByte,
    bool SkipsFinalRealignment,
    string ObservationKind);
