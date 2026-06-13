using NesSharp.Core.Runtime;

namespace NesSharp.Core.Testing;

public static class LegacyPpuTestRunner
{
    private const ushort ResultAddress = 0x00F0;
    private const byte JmpAbsoluteOpcode = 0x4C;

    public static SpriteResultTestResult Run(NesMachine machine, long maxInstructions)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (maxInstructions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInstructions), "Maximum instruction count must be positive.");
        }

        machine.Reset();

        for (long instructions = 0; instructions < maxInstructions; instructions++)
        {
            if (IsAtFinalLoop(machine))
            {
                return new SpriteResultTestResult(
                    SpriteResultTestStatus.Completed,
                    machine.CpuBus.ReadRaw(ResultAddress),
                    instructions,
                    machine.Cpu.Cycles);
            }

            try
            {
                machine.StepInstruction();
            }
            catch
            {
                return new SpriteResultTestResult(
                    SpriteResultTestStatus.ExecutionError,
                    machine.CpuBus.ReadRaw(ResultAddress),
                    instructions,
                    machine.Cpu.Cycles);
            }
        }

        return new SpriteResultTestResult(
            SpriteResultTestStatus.Timeout,
            machine.CpuBus.ReadRaw(ResultAddress),
            maxInstructions,
            machine.Cpu.Cycles);
    }

    private static bool IsAtFinalLoop(NesMachine machine)
    {
        var pc = machine.Cpu.ProgramCounter;
        return machine.CpuBus.ReadRaw(ResultAddress) != 0 &&
            machine.CpuBus.ReadRaw(pc) == JmpAbsoluteOpcode &&
            machine.CpuBus.ReadWordRaw((ushort)(pc + 1)) == pc;
    }
}
