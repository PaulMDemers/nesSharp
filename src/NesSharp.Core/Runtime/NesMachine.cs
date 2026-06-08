using NesSharp.Core.Cartridge;
using NesSharp.Core.Cpu;
using NesSharp.Core.Memory;
using NesSharp.Core.Ppu;

namespace NesSharp.Core.Runtime;

public sealed class NesMachine
{
    public NesMachine(Cartridge.Cartridge cartridge)
    {
        Cartridge = cartridge;
        PpuBus = new PpuBus(cartridge);
        CpuBus = new CpuBus(cartridge, PpuBus);
        Cpu = new Cpu6502(CpuBus);
    }

    public Cartridge.Cartridge Cartridge { get; }

    public CpuBus CpuBus { get; }

    public PpuBus PpuBus { get; }

    public Cpu6502 Cpu { get; }

    public static NesMachine LoadFile(string path)
    {
        return new NesMachine(INesRomLoader.LoadFile(path));
    }

    public void Reset()
    {
        PpuBus.Reset();
        Cpu.Reset();
    }

    public int StepInstruction()
    {
        var cpuCycles = Cpu.Step();
        PpuBus.Clock(cpuCycles * 3);
        if (PpuBus.PollNmi())
        {
            Cpu.RequestNmi();
        }

        return cpuCycles;
    }
}
