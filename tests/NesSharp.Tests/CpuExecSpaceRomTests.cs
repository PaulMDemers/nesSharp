using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;

namespace NesSharp.Tests;

public sealed class CpuExecSpaceRomTests
{
    public static TheoryData<string> PassingCpuExecSpaceRoms => new()
    {
        "test_cpu_exec_space_apu.nes",
        "test_cpu_exec_space_ppuio.nes"
    };

    [Theory]
    [MemberData(nameof(PassingCpuExecSpaceRoms))]
    public void CpuExecSpaceRomsPass(string romName)
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "cpu_exec_space", romName);

        var result = BlarggTestRunner.Run(NesMachine.LoadFile(romPath), 100_000_000);

        Assert.True(
            result.Passed,
            $"{romName} failed with status {result.Status}, code {result.ResultCode:X2}, after {result.InstructionsExecuted} instructions.{Environment.NewLine}{result.Output}");
    }

    private static string FindWorkspaceRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "cpu_exec_space")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing cpu_exec_space test ROMs.");
    }
}
