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

    public CpuBus(Cartridge.Cartridge cartridge)
        : this(cartridge, new PpuBus(cartridge))
    {
    }

    public CpuBus(Cartridge.Cartridge cartridge, PpuBus ppuBus)
    {
        Cartridge = cartridge;
        this.ppuBus = ppuBus;
    }

    public Cartridge.Cartridge Cartridge { get; }

    public StandardController Controller1 { get; } = new();

    public StandardController Controller2 { get; } = new();

    public ApuBus ApuBus { get; } = new();

    public int CpuAccessCycles { get; private set; }

    public int InstructionAccessCycles => instructionAccessCycles;

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
        RunPendingDmcDma();
        return value;
    }

    public byte ReadRaw(ushort address)
    {
        return address switch
        {
            <= 0x1FFF => ram[address & 0x07FF],
            >= 0x2000 and <= 0x3FFF => ppuBus.ReadRegister((ushort)(0x2000 + (address & 0x0007))),
            0x4015 => ApuBus.ReadStatus(),
            0x4016 => Controller1.Read(),
            0x4017 => Controller2.Read(),
            >= 0x4000 and <= 0x401F => 0,
            >= 0x4020 => Cartridge.CpuRead(address)
        };
    }

    public void Write(ushort address, byte value)
    {
        ClockCpuAccess();
        WriteRaw(address, value);
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

    private void ClockCpuAccess(bool instructionAccess = true)
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
        nextDmaCycleIsGet = !nextDmaCycleIsGet;
    }

    private void RunPendingDmcDma()
    {
        if (!ApuBus.IsDmcDmaPending)
        {
            return;
        }

        var address = ApuBus.PendingDmcDmaAddress;
        var cycles = ApuBus.PendingDmcDmaCycles;
        for (var i = 0; i < cycles; i++)
        {
            ClockCpuAccess(instructionAccess: false);
        }

        ApuBus.CompleteDmcDma(ReadRaw(address));
    }

    private void RunOamDma(byte page)
    {
        var baseAddress = page << 8;
        for (var i = 0; i < 256; i++)
        {
            ppuBus.WriteOamDmaByte(ReadRaw((ushort)(baseAddress + i)));
        }

        var dmaCycles = nextDmaCycleIsGet ? 514 : 513;
        for (var i = 0; i < dmaCycles; i++)
        {
            ClockCpuAccess(instructionAccess: false);
        }
    }
}
