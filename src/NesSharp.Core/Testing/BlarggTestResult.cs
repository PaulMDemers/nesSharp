namespace NesSharp.Core.Testing;

public sealed record BlarggTestResult(
    BlarggTestStatus Status,
    byte? ResultCode,
    string Output,
    long InstructionsExecuted,
    ulong CpuCycles)
{
    public bool Passed => Status == BlarggTestStatus.Completed && ResultCode == 0;
}

