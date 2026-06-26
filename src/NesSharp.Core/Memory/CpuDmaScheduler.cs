namespace NesSharp.Core.Memory;

public static class CpuDmaScheduler
{
    public static CpuDmaSchedule Simulate(CpuDmaScheduleRequest request)
    {
        var events = new List<CpuDmaCycleEvent>();
        var nextCycleIsGet = request.FirstCycleIsGet;
        var oamActive = request.OamByteCount > 0;
        var oamNeedsHalt = oamActive;
        var oamOperationCount = 0;
        var oamReadAddress = 0;
        var dmcActive = request.DmcStartCycle is 0;
        var dmcNeedsHalt = dmcActive;
        var dmcNeedsDummy = dmcActive;
        var dmcCompleted = false;
        var cycle = 0;

        while (oamActive || dmcActive)
        {
            if (request.DmcStartCycle == cycle)
            {
                dmcActive = true;
                dmcNeedsHalt = true;
                dmcNeedsDummy = true;
            }

            var cycleIsGet = nextCycleIsGet;
            CpuDmaCycleKind kind;
            int? oamIndex = null;

            if (cycleIsGet)
            {
                if (dmcActive && !dmcNeedsHalt && !dmcNeedsDummy)
                {
                    kind = CpuDmaCycleKind.DmcRead;
                    dmcActive = false;
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

            if (dmcActive)
            {
                if (dmcNeedsHalt)
                {
                    dmcNeedsHalt = false;
                }
                else if (dmcNeedsDummy)
                {
                    dmcNeedsDummy = false;
                }
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
    int? DmcStartCycle);

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
