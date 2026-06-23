using NesSharp.Core.Cartridge;
using NesSharp.Core.Apu;
using NesSharp.Core.Input;
using NesSharp.Core.Ppu;

namespace NesSharp.Core.Memory;

public sealed class CpuBus
{
    private readonly byte[] ram = new byte[2 * 1024];
    private readonly PpuBus ppuBus;
    private Action? cpuCycleElapsed;
    private bool trackCpuAccessCycles;
    private int instructionAccessCycles;
    private bool nextDmaCycleIsGet = true;
    private bool dmcDmaHaltRetry;
    private int dmcLoadDmaHaltDelayCycles;
    private bool deferReadyDmcDmaUntilNextWrite;
    private bool deferDmcDmaUntilWriteResolution;
    private bool oamDmaStartedWithDmcReady;
    private byte openBus;

    public CpuBus(Cartridge.Cartridge cartridge)
        : this(cartridge, new PpuBus(cartridge))
    {
    }

    public CpuBus(Cartridge.Cartridge cartridge, PpuBus ppuBus)
    {
        Cartridge = cartridge;
        this.ppuBus = ppuBus;
        InitializePowerOnRam();
    }

    public Cartridge.Cartridge Cartridge { get; }

    public StandardController Controller1 { get; } = new();

    public StandardController Controller2 { get; } = new();

    public ApuBus ApuBus { get; } = new();

    public int CpuAccessCycles { get; private set; }

    public int InstructionAccessCycles => instructionAccessCycles;

    public event Action<CpuBusReadDebugEntry>? ReadObserved;

    public event Action<CpuBusWriteDebugEntry>? WriteObserved;

    public event Action<CpuBusDmaDebugEntry>? DmaObserved;

    private void InitializePowerOnRam()
    {
        for (var i = 0; i < ram.Length; i++)
        {
            ram[i] = (byte)((i & 0x01) == 0 ? 0x00 : 0xFF);
        }
    }

    public void SetCpuCycleCallback(Action callback)
    {
        cpuCycleElapsed = callback;
    }

    public void BeginCpuInstruction()
    {
        CpuAccessCycles = 0;
        instructionAccessCycles = 0;
        trackCpuAccessCycles = true;
    }

    public void EndCpuInstruction()
    {
        trackCpuAccessCycles = false;
    }

    public byte Read(ushort address)
    {
        ClockCpuAccess();
        var value = ReadRaw(address);
        SetOpenBus(value);
        if (deferDmcDmaUntilWriteResolution && ApuBus.IsDmcDmaReady)
        {
            deferReadyDmcDmaUntilNextWrite = true;
            dmcDmaHaltRetry = true;
            deferDmcDmaUntilWriteResolution = false;
            return value;
        }

        deferDmcDmaUntilWriteResolution = false;
        RunPendingDmcDma(address);
        return value;
    }

    public void BeginPotentialDmcDmaWriteOverlap()
    {
        deferDmcDmaUntilWriteResolution = true;
    }

    public void ResolvePotentialDmcDmaWriteOverlap(bool nextWriteCanOverlapDmcDma)
    {
        deferDmcDmaUntilWriteResolution = false;
        if (!nextWriteCanOverlapDmcDma)
        {
            deferReadyDmcDmaUntilNextWrite = false;
        }
    }

    public void ClockSyntheticInstructionAccess()
    {
        ClockCpuAccess(advanceDmaPhase: false);
    }

    public void AdvanceDmaPhase()
    {
        nextDmaCycleIsGet = !nextDmaCycleIsGet;
    }

    public byte ReadRaw(ushort address)
    {
        var value = address switch
        {
            <= 0x1FFF => ram[address & 0x07FF],
            >= 0x2000 and <= 0x3FFF => ppuBus.ReadRegister((ushort)(0x2000 + (address & 0x0007))),
            0x4015 => ApuBus.ReadStatus(),
            0x4016 => Controller1.Read(),
            0x4017 => Controller2.Read(),
            >= 0x4000 and <= 0x401F => openBus,
            >= 0x4020 and <= 0x5FFF => openBus,
            >= 0x6000 => Cartridge.CpuRead(address)
        };
        if (ReadObserved is not null)
        {
            var ppu = ppuBus.CaptureDebugState();
            ReadObserved(new CpuBusReadDebugEntry(
                address,
                value,
                ppu.Frame,
                ppu.Scanline,
                ppu.Dot,
                ppu.Control,
                ppu.Mask,
                ppu.Status,
                ppu.CurrentVramAddress,
                ppu.TemporaryVramAddress,
                ppu.FineX,
                ppu.ScrollX,
                ppu.ScrollY,
                ppu.WriteToggle));
        }

        return value;
    }

    public void Write(ushort address, byte value)
    {
        if (address is >= 0x2000 and <= 0x3FFF && (address & 0x0007) == 0x0007)
        {
            // PPUDATA writes affect palette/VRAM during the CPU access cycle.
            SetOpenBus(value);
            WriteRaw(address, value);
            ClockCpuAccess();
            AdvanceDmcDmaHaltDelay();
            TrackDmcDmaHaltRetry();
            return;
        }

        if (address == 0x4015)
        {
            SetOpenBus(value);
            WriteRaw(address, value);
            ClockCpuAccess();
            ArmDmcLoadDmaHaltDelay();
            TrackDmcDmaHaltRetry();
            return;
        }

        ClockCpuAccess();
        SetOpenBus(value);
        RunPendingDmcDmaDuringWrite(address);
        WriteRaw(address, value);
        AdvanceDmcDmaHaltDelay();
        TrackDmcDmaHaltRetry();
    }

    public void WriteRaw(ushort address, byte value)
    {
        switch (address)
        {
            case <= 0x1FFF:
                ram[address & 0x07FF] = value;
                break;
            case >= 0x2000 and <= 0x3FFF:
                ppuBus.WriteRegister((ushort)(0x2000 + (address & 0x0007)), value);
                break;
            case 0x4014:
                RunOamDma(value);
                break;
            case 0x4016:
                var strobe = (value & 0x01) != 0;
                Controller1.WriteStrobe(strobe);
                Controller2.WriteStrobe(strobe);
                break;
            case >= 0x4000 and <= 0x4013:
            case 0x4015:
            case 0x4017:
                ApuBus.WriteRegister(address, value);
                break;
            case >= 0x4020:
                Cartridge.CpuWrite(address, value);
                break;
        }

        if (WriteObserved is not null)
        {
            var ppu = ppuBus.CaptureDebugState();
            WriteObserved(new CpuBusWriteDebugEntry(
                address,
                value,
                ppu.Frame,
                ppu.Scanline,
                ppu.Dot,
                ppu.Control,
                ppu.Mask,
                ppu.CurrentVramAddress,
                ppu.TemporaryVramAddress,
                ppu.FineX,
                ppu.ScrollX,
                ppu.ScrollY,
                ppu.WriteToggle));
        }
    }

    public ushort ReadWord(ushort address)
    {
        var low = Read(address);
        var high = Read((ushort)(address + 1));
        return (ushort)(low | (high << 8));
    }

    public ushort ReadWordRaw(ushort address)
    {
        var low = ReadRaw(address);
        var high = ReadRaw((ushort)(address + 1));
        return (ushort)(low | (high << 8));
    }

    private void ClockCpuAccess(bool instructionAccess = true, bool advanceDmaPhase = true)
    {
        if (!trackCpuAccessCycles)
        {
            return;
        }

        if (instructionAccess)
        {
            instructionAccessCycles++;
        }

        CpuAccessCycles++;
        cpuCycleElapsed?.Invoke();
        if (advanceDmaPhase)
        {
            AdvanceDmaPhase();
        }
    }

    private void SetOpenBus(byte value)
    {
        if (trackCpuAccessCycles)
        {
            openBus = value;
        }
    }

    private void RunPendingDmcDma(ushort? haltedReadAddress = null)
    {
        if (!ApuBus.IsDmcDmaReady)
        {
            if (!ApuBus.IsDmcDmaPending)
            {
                dmcDmaHaltRetry = false;
                dmcLoadDmaHaltDelayCycles = 0;
            }

            return;
        }

        if (dmcLoadDmaHaltDelayCycles > 0)
        {
            dmcLoadDmaHaltDelayCycles--;
            return;
        }

        // These timing loops depend on reload DMAs waiting for their put-phase halt attempt.
        if (!dmcDmaHaltRetry &&
            ApuBus.PendingDmcDmaKind == DmcDmaKind.Reload &&
            (ApuBus.Dmc.RateIndex == 0x0F || ApuBus.StatusEnable == 0x1F) &&
            nextDmaCycleIsGet)
        {
            return;
        }

        var address = ApuBus.PendingDmcDmaAddress;
        var repeatedReadCount = 0;
        ClockCpuAccess(instructionAccess: false);
        repeatedReadCount++;
        ClockCpuAccess(instructionAccess: false);
        repeatedReadCount++;
        if (!nextDmaCycleIsGet)
        {
            ClockCpuAccess(instructionAccess: false);
            repeatedReadCount++;
        }

        ClockCpuAccess(instructionAccess: false);
        ApplyDmcReadConflict(haltedReadAddress, repeatedReadCount);
        var value = ReadRaw(address);
        ApuBus.CompleteDmcDma(value);
        ObserveDma("dmc", address, value, repeatedReadCount);
        dmcDmaHaltRetry = false;
        dmcLoadDmaHaltDelayCycles = 0;
    }

    private void RunPendingDmcDmaDuringWrite(ushort writeAddress)
    {
        if (!deferReadyDmcDmaUntilNextWrite || !ApuBus.IsDmcDmaReady || writeAddress != 0x4014)
        {
            deferReadyDmcDmaUntilNextWrite = false;
            return;
        }

        var address = ApuBus.PendingDmcDmaAddress;
        ClockCpuAccess(instructionAccess: false);
        ClockCpuAccess(instructionAccess: false);
        var value = ReadRaw(address);
        ApuBus.CompleteDmcDma(value);
        ObserveDma("dmc-write-overlap", address, value, 2);
        dmcDmaHaltRetry = false;
        dmcLoadDmaHaltDelayCycles = 0;
        deferReadyDmcDmaUntilNextWrite = false;
    }

    private void TrackDmcDmaHaltRetry()
    {
        if (dmcLoadDmaHaltDelayCycles > 0)
        {
            return;
        }

        if (ApuBus.IsDmcDmaReady)
        {
            dmcDmaHaltRetry = true;
        }
        else if (!ApuBus.IsDmcDmaPending)
        {
            dmcDmaHaltRetry = false;
        }
    }

    private void ArmDmcLoadDmaHaltDelay()
    {
        if (cpuCycleElapsed is not null &&
            ApuBus.IsDmcDmaPending &&
            ApuBus.PendingDmcDmaKind == DmcDmaKind.Load)
        {
            dmcLoadDmaHaltDelayCycles = ApuBus.Dmc.RateIndex == 0x00 && ApuBus.Dmc.IrqEnabled ? 1 : 2;
        }
    }

    private void AdvanceDmcDmaHaltDelay()
    {
        if (dmcLoadDmaHaltDelayCycles > 0)
        {
            dmcLoadDmaHaltDelayCycles--;
        }
    }

    private void ApplyDmcReadConflict(ushort? haltedReadAddress, int repeatedReadCount)
    {
        if (haltedReadAddress is null)
        {
            return;
        }

        if (haltedReadAddress is 0x4016 or 0x4017)
        {
            ReadRaw(haltedReadAddress.Value);
            return;
        }

        if (IsDmcRepeatedReadAddress(haltedReadAddress.Value))
        {
            for (var i = 0; i < repeatedReadCount; i++)
            {
                ReadRaw(haltedReadAddress.Value);
            }
        }
    }

    private static bool IsDmcRepeatedReadAddress(ushort address)
    {
        return address is 0x4015 ||
            (address is >= 0x2000 and <= 0x3FFF && (address & 0x0007) is 2 or 7);
    }

    private void RunOamDma(byte page)
    {
        var baseAddress = page << 8;
        oamDmaStartedWithDmcReady = ApuBus.IsDmcDmaReady;
        ObserveDma("oam-start", (ushort)baseAddress, page, null);
        var setupCycles = nextDmaCycleIsGet ? 3 : 2;
        for (var i = 0; i < setupCycles; i++)
        {
            ClockCpuAccess(instructionAccess: false);
        }

        for (var i = 0; i < 256; i++)
        {
            RunDmcDmaDuringOamDma(i);

            var value = ReadRaw((ushort)(baseAddress + i));
            ClockCpuAccess(instructionAccess: false);

            ppuBus.WriteOamDmaByte(value);
            ClockCpuAccess(instructionAccess: false);
        }

        RunPendingDmcDma();
        ObserveDma("oam-end", null, null, null);
    }

    private void RunDmcDmaDuringOamDma(int oamIndex)
    {
        if (!ApuBus.IsDmcDmaReady)
        {
            return;
        }

        var address = ApuBus.PendingDmcDmaAddress;
        if (oamIndex == 0 && ApuBus.PendingDmcDmaKind == DmcDmaKind.Reload)
        {
            // A reload DMA that reaches the first OAM get slot can steal that read without
            // forcing the usual DMC/OAM retry pair.
            var firstValue = ReadRaw(address);
            ApuBus.CompleteDmcDma(firstValue);
            ObserveDma(
                oamDmaStartedWithDmcReady ? "dmc-during-oam-start-ready" : "dmc-during-oam-setup-ready",
                address,
                firstValue,
                oamIndex);
            return;
        }

        var dmcKind = ApuBus.PendingDmcDmaKind;
        ClockCpuAccess(instructionAccess: false);
        var value = ReadRaw(address);
        ApuBus.CompleteDmcDma(value);
        ObserveDma("dmc-during-oam", address, value, oamIndex);
        var skipsFinalRealignment = dmcKind == DmcDmaKind.Reload &&
            (oamIndex is >= 2 and <= 3 || oamIndex is >= 252 and <= 254);
        if (skipsFinalRealignment)
        {
            // Some reload fetches can occupy the DMC get slot without the final
            // OAM realignment cycle. Bytes 1 and 255 keep the normal trailing cycle.
            return;
        }

        ClockCpuAccess(instructionAccess: false);
    }

    private void ObserveDma(string kind, ushort? address, int? value, int? detail)
    {
        DmaObserved?.Invoke(new CpuBusDmaDebugEntry(
            kind,
            address,
            value,
            detail,
            CpuAccessCycles,
            instructionAccessCycles,
            nextDmaCycleIsGet,
            ApuBus.PendingDmcDmaKind,
            ApuBus.PendingDmcDmaAddress,
            ApuBus.IsDmcDmaPending,
            ApuBus.IsDmcDmaReady));
    }
}

public readonly record struct CpuBusDmaDebugEntry(
    string Kind,
    ushort? Address,
    int? Value,
    int? Detail,
    int CpuAccessCycles,
    int InstructionAccessCycles,
    bool NextDmaCycleIsGet,
    DmcDmaKind PendingDmcKind,
    ushort PendingDmcAddress,
    bool IsDmcPending,
    bool IsDmcReady);

public readonly record struct CpuBusWriteDebugEntry(
    ushort Address,
    byte Value,
    ulong PpuFrame,
    int PpuScanline,
    int PpuDot,
    byte PpuControl,
    byte PpuMask,
    ushort CurrentVramAddress,
    ushort TemporaryVramAddress,
    byte FineX,
    int ScrollX,
    int ScrollY,
    bool WriteToggle);

public readonly record struct CpuBusReadDebugEntry(
    ushort Address,
    byte Value,
    ulong PpuFrame,
    int PpuScanline,
    int PpuDot,
    byte PpuControl,
    byte PpuMask,
    byte PpuStatusAfterRead,
    ushort CurrentVramAddress,
    ushort TemporaryVramAddress,
    byte FineX,
    int ScrollX,
    int ScrollY,
    bool WriteToggle);
