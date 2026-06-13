using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;

namespace NesSharp.Tests;

public sealed class CpuDummyWriteRomTests
{
    public static TheoryData<string> PassingCpuDummyWriteRoms => new()
    {
        "cpu_dummy_writes_oam.nes",
        "cpu_dummy_writes_ppumem.nes"
    };

    [Theory]
    [MemberData(nameof(PassingCpuDummyWriteRoms))]
    public void CpuDummyWriteRomsPass(string romName)
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "cpu_dummy_writes", romName);

        var result = BlarggTestRunner.Run(NesMachine.LoadFile(romPath), 150_000_000);

        Assert.True(
            result.Passed,
            $"{romName} failed with status {result.Status}, code {result.ResultCode:X2}, after {result.InstructionsExecuted} instructions.{Environment.NewLine}{result.Output}");
    }

    private static string FindWorkspaceRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "cpu_dummy_writes")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing cpu_dummy_writes.");
    }
}
