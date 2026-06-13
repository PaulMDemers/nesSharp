using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;

namespace NesSharp.Tests;

public sealed class CpuDummyReadRomTests
{
    [Fact]
    public void CpuDummyReadsRomPasses()
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "cpu_dummy_reads", "cpu_dummy_reads.nes");

        var result = ShellExitTestRunner.Run(NesMachine.LoadFile(romPath), 100_000_000);

        Assert.True(
            result.Passed,
            $"cpu_dummy_reads.nes failed with status {result.Status}, code {result.ResultCode:X2}, after {result.InstructionsExecuted} instructions.");
    }

    private static string FindWorkspaceRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "cpu_dummy_reads", "cpu_dummy_reads.nes")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing cpu_dummy_reads.nes.");
    }
}
