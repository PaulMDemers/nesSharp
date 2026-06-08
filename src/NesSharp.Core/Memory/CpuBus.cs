using NesSharp.Core.Cartridge;
using NesSharp.Core.Ppu;

namespace NesSharp.Core.Memory;

public sealed class CpuBus
{
    private readonly byte[] ram = new byte[2 * 1024];
    private readonly PpuBus ppuBus;
    private Action? cpuCycleElapsed;
    private bool trackCpuAccessCycles;

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

    public int CpuAccessCycles { get; private set; }

    public void SetCpuCycleCallback(Action callback)
    {
        cpuCycleElapsed = callback;
    }

    public void BeginCpuInstruction()
    {
        CpuAccessCycles = 0;
        trackCpuAccessCycles = true;
    }

    public void EndCpuInstruction()
    {
        trackCpuAccessCycles = false;
    }

    public byte Read(ushort address)
    {
        ClockCpuAccess();
        return ReadRaw(address);
    }

    public byte ReadRaw(ushort address)
    {
        return address switch
        {
            <= 0x1FFF => ram[address & 0x07FF],
            >= 0x2000 and <= 0x3FFF => ppuBus.ReadRegister((ushort)(0x2000 + (address & 0x0007))),
            >= 0x4020 => Cartridge.CpuRead(address),
            _ => 0
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

    private void ClockCpuAccess()
    {
        if (!trackCpuAccessCycles)
        {
            return;
        }

        CpuAccessCycles++;
        cpuCycleElapsed?.Invoke();
    }
}
