namespace NesSharp.Core.Testing;

public sealed record SpriteResultTestResult(
    SpriteResultTestStatus Status,
    byte? ResultCode,
    long InstructionsExecuted,
    ulong CpuCycles)
{
    public bool Passed => Status == SpriteResultTestStatus.Completed && ResultCode == 1;
}

