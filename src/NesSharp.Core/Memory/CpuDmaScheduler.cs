namespace NesSharp.Core.Memory;

public static class CpuDmaScheduler
{
    public static CpuDmaSchedule Simulate(CpuDmaScheduleRequest request)
    {
        var events = new List<CpuDmaCycleEvent>();
        var nextCycleIsGet = request.FirstCycleIsGet;
        var oamActive = request.OamByteCount > 0;
        var oamNeedsHalt = oamActive && !request.OamHaltAlreadyDone;
        var oamOperationCount = 0;
        var oamReadAddress = 0;
        var dmcState = request.DmcInitialState;
        if (request.DmcStartCycle is 0 && dmcState == CpuDmaDmcState.Inactive)
        {
            dmcState = request.DmcStartState;
        }

        var dmcCompleted = false;
        var cycle = 0;

        while (oamActive || dmcState != CpuDmaDmcState.Inactive)
        {
            if (request.DmcStartCycle == cycle && dmcState == CpuDmaDmcState.Inactive)
            {
                dmcState = request.DmcStartState;
            }

            var cycleIsGet = nextCycleIsGet;
            CpuDmaCycleKind kind;
            int? oamIndex = null;

            if (cycleIsGet)
            {
                if (dmcState == CpuDmaDmcState.ReadyToRead)
                {
                    kind = CpuDmaCycleKind.DmcRead;
                    dmcState = CpuDmaDmcState.Inactive;
                    dmcCompleted = true;
                }
                else if (oamNeedsHalt)
                {
                    kind = CpuDmaCycleKind.DummyRead;
                    oamNeedsHalt = false;
                }
                else if (oamActive)
                {
                    kind = CpuDmaCycleKind.OamRead;
                    oamIndex = oamReadAddress;
                    oamReadAddress++;
                    oamOperationCount++;
                }
                else
                {
                    kind = CpuDmaCycleKind.DummyRead;
                }
            }
            else if (oamNeedsHalt)
            {
                kind = CpuDmaCycleKind.DummyRead;
                oamNeedsHalt = false;
            }
            else if (oamActive && (oamOperationCount & 0x01) != 0)
            {
                kind = CpuDmaCycleKind.OamWrite;
                oamIndex = oamReadAddress - 1;
                oamOperationCount++;
                if (oamOperationCount == request.OamByteCount * 2)
                {
                    oamActive = false;
                }
            }
            else
            {
                kind = CpuDmaCycleKind.DummyRead;
            }

            if (dmcState == CpuDmaDmcState.NeedHalt)
            {
                dmcState = CpuDmaDmcState.NeedDummy;
            }
            else if (dmcState == CpuDmaDmcState.NeedDummy)
            {
                dmcState = CpuDmaDmcState.ReadyToRead;
            }

            events.Add(new CpuDmaCycleEvent(cycle, cycleIsGet, kind, oamIndex));
            cycle++;
            nextCycleIsGet = !nextCycleIsGet;
        }

        return new CpuDmaSchedule(events, dmcCompleted);
    }
}

public readonly record struct CpuDmaScheduleRequest(
    bool FirstCycleIsGet,
    int OamByteCount,
    int? DmcStartCycle,
    bool OamHaltAlreadyDone = false,
    CpuDmaDmcState DmcInitialState = CpuDmaDmcState.Inactive,
    CpuDmaDmcState DmcStartState = CpuDmaDmcState.NeedHalt);

public sealed class CpuDmaSchedule(IReadOnlyList<CpuDmaCycleEvent> events, bool dmcCompleted)
{
    public IReadOnlyList<CpuDmaCycleEvent> Events { get; } = events;

    public bool DmcCompleted { get; } = dmcCompleted;

    public int CycleCount => Events.Count;
}

public readonly record struct CpuDmaCycleEvent(
    int Cycle,
    bool IsGetCycle,
    CpuDmaCycleKind Kind,
    int? OamIndex);

public enum CpuDmaCycleKind
{
    DummyRead,
    OamRead,
    OamWrite,
    DmcRead
}

public enum CpuDmaDmcState
{
    Inactive,
    NeedHalt,
    NeedDummy,
    ReadyToRead
}
