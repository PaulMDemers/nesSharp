using NesSharp.Core.Runtime;

namespace NesSharp.Core.Testing;

public static class ShellExitTestRunner
{
    public static ShellExitTestResult Run(
        NesMachine machine,
        long maxInstructions,
        Action<NesMachine, long>? beforeInstruction = null)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (maxInstructions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInstructions), "Maximum instruction count must be positive.");
        }

        machine.Reset();

        for (long instructions = 0; instructions < maxInstructions; instructions++)
        {
            if (IsAtShellExit(machine))
            {
                return new ShellExitTestResult(
                    ShellExitTestStatus.Completed,
                    machine.Cpu.A,
                    instructions,
                    machine.Cpu.Cycles);
            }

            beforeInstruction?.Invoke(machine, instructions);

            try
            {
                machine.StepInstruction();
            }
            catch
            {
                return new ShellExitTestResult(
                    ShellExitTestStatus.ExecutionError,
                    null,
                    instructions,
                    machine.Cpu.Cycles);
            }
        }

        return new ShellExitTestResult(
            ShellExitTestStatus.Timeout,
            null,
            maxInstructions,
            machine.Cpu.Cycles);
    }

    private static bool IsAtShellExit(NesMachine machine)
    {
        var pc = machine.Cpu.ProgramCounter;
        return machine.CpuBus.ReadRaw(pc) == 0xA2 &&
            machine.CpuBus.ReadRaw((ushort)(pc + 1)) == 0xFF &&
            machine.CpuBus.ReadRaw((ushort)(pc + 2)) == 0x9A &&
            machine.CpuBus.ReadRaw((ushort)(pc + 3)) == 0x78 &&
            machine.CpuBus.ReadRaw((ushort)(pc + 4)) == 0x48;
    }
}
