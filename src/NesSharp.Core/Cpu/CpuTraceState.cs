namespace NesSharp.Core.Cpu;

public sealed record CpuTraceState(
    ushort ProgramCounter,
    byte Opcode,
    byte Operand1,
    byte Operand2,
    int InstructionLength,
    byte A,
    byte X,
    byte Y,
    byte Status,
    byte StackPointer,
    ulong Cycles)
{
    public int PpuScanline => (int)((Cycles * 3 / 341) % 262);

    public int PpuDot => (int)((Cycles * 3) % 341);

    public string ToNestestStateString()
    {
        return $"PC:{ProgramCounter:X4} OP:{Opcode:X2} A:{A:X2} X:{X:X2} Y:{Y:X2} P:{Status:X2} SP:{StackPointer:X2} CYC:{Cycles}";
    }
}

