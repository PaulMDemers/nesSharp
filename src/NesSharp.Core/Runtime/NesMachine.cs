using NesSharp.Core.Cartridge;
using NesSharp.Core.Cpu;
using NesSharp.Core.Input;
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
        CpuBus.SetCpuCycleCallback(() => ClockOneCpuCycle());
        PpuBus.NmiSuppressed += Cpu.ClearPendingNmi;
    }

    public Cartridge.Cartridge Cartridge { get; }

    public CpuBus CpuBus { get; }

    public PpuBus PpuBus { get; }

    public Cpu6502 Cpu { get; }

    public StandardController Controller1 => CpuBus.Controller1;

    public StandardController Controller2 => CpuBus.Controller2;

    private bool requestIrqAfterCurrentInstruction;

    public static NesMachine LoadFile(string path)
    {
        return new NesMachine(INesRomLoader.LoadFile(path));
    }

    public void Reset()
    {
        PpuBus.Reset();
        CpuBus.ApuBus.Reset();
        Cpu.Reset();
    }

    public void SoftReset()
    {
        CpuBus.ApuBus.SoftReset();
        Cpu.Reset();
    }

    public int StepInstruction()
    {
        var cpuCycles = Cpu.Step();
        if (requestIrqAfterCurrentInstruction && (CpuBus.ApuBus.IsFrameInterruptPending || CpuBus.ApuBus.IsDmcInterruptPending))
        {
            Cpu.RequestIrq();
        }

        requestIrqAfterCurrentInstruction = false;
        var remainingCycles = cpuCycles - CpuBus.InstructionAccessCycles;
        if (remainingCycles > 0)
        {
            for (var i = 0; i < remainingCycles; i++)
            {
                ClockOneCpuCycle(deferNewIrq: true);
            }
        }

        return cpuCycles;
    }

    private void ClockOneCpuCycle(bool deferNewIrq = false)
    {
        CpuBus.ApuBus.Clock();
        if (CpuBus.ApuBus.IsFrameInterruptPending || CpuBus.ApuBus.IsDmcInterruptPending)
        {
            if (deferNewIrq && !Cpu.IsIrqRequested)
            {
                requestIrqAfterCurrentInstruction = true;
            }
            else
            {
                RequestCpuIrq(deferNewIrq);
            }
        }
        else
        {
            Cpu.ClearPendingIrq();
        }

        PpuBus.Clock(3);
        if (PpuBus.PollNmi())
        {
            Cpu.RequestNmi();
        }
    }

    private void RequestCpuIrq(bool delayed)
    {
        if (delayed)
        {
            Cpu.RequestIrqDelayed();
        }
        else
        {
            Cpu.RequestIrq();
        }
    }
}
