using System.Text;
using NesSharp.Core.Runtime;

namespace NesSharp.Core.Testing;

public static class BlarggTestRunner
{
    private const byte JmpAbsoluteOpcode = 0x4C;
    private const byte RunningStatus = 0x80;
    private const byte ResetRequestedStatus = 0x81;
    private const int MaxResetRequests = 8;
    private const ushort StatusAddress = 0x6000;
    private const ushort SignatureAddress = 0x6001;
    private const ushort TextAddress = 0x6004;
    private const int MaxTextLength = 0x1FFC;

    public static BlarggTestResult Run(NesMachine machine, long maxInstructions)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (maxInstructions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInstructions), "Maximum instruction count must be positive.");
        }

        machine.Reset();
        var resetRequests = 0;

        for (long instructions = 0; instructions < maxInstructions; instructions++)
        {
            if (HasSignature(machine))
            {
                var status = machine.CpuBus.Read(StatusAddress);
                if (status <= 0x7F)
                {
                    return new BlarggTestResult(
                        BlarggTestStatus.Completed,
                        status,
                        ReadOutput(machine),
                        instructions,
                        machine.Cpu.Cycles);
                }

                if (status == ResetRequestedStatus)
                {
                    if (!IsAtSelfJumpLoop(machine))
                    {
                        machine.StepInstruction();
                        continue;
                    }

                    if (resetRequests >= MaxResetRequests)
                    {
                        return new BlarggTestResult(
                            BlarggTestStatus.ResetRequested,
                            status,
                            ReadOutput(machine),
                            instructions,
                            machine.Cpu.Cycles);
                    }

                    resetRequests++;
                    machine.SoftReset();
                    machine.CpuBus.WriteRaw(StatusAddress, RunningStatus);
                    continue;
                }
            }

            try
            {
                machine.StepInstruction();
            }
            catch (Exception ex)
            {
                return new BlarggTestResult(
                    BlarggTestStatus.ExecutionError,
                    null,
                    ex.Message,
                    instructions,
                    machine.Cpu.Cycles);
            }
        }

        return new BlarggTestResult(
            BlarggTestStatus.Timeout,
            HasSignature(machine) ? machine.CpuBus.Read(StatusAddress) : null,
            HasSignature(machine) ? ReadOutput(machine) : string.Empty,
            maxInstructions,
            machine.Cpu.Cycles);
    }

    private static bool HasSignature(NesMachine machine)
    {
        return machine.CpuBus.Read(SignatureAddress) == 0xDE &&
            machine.CpuBus.Read(SignatureAddress + 1) == 0xB0 &&
            machine.CpuBus.Read(SignatureAddress + 2) == 0x61;
    }

    private static bool IsAtSelfJumpLoop(NesMachine machine)
    {
        var pc = machine.Cpu.ProgramCounter;
        return machine.CpuBus.ReadRaw(pc) == JmpAbsoluteOpcode &&
            machine.CpuBus.ReadWordRaw((ushort)(pc + 1)) == pc;
    }

    private static string ReadOutput(NesMachine machine)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < MaxTextLength; i++)
        {
            var value = machine.CpuBus.Read((ushort)(TextAddress + i));
            if (value == 0)
            {
                break;
            }

            builder.Append(value is >= 0x20 and <= 0x7E or 0x0A or 0x0D or 0x09
                ? (char)value
                : '.');
        }

        return builder.ToString();
    }
}
