namespace NesSharp.Core.Testing;

public sealed record ShellExitTestResult(
    ShellExitTestStatus Status,
    byte? ResultCode,
    long InstructionsExecuted,
    ulong CpuCycles)
{
    public bool Passed => Status == ShellExitTestStatus.Completed && ResultCode == 0;
}
