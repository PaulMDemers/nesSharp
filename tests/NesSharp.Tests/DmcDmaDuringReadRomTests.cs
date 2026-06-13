using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;

namespace NesSharp.Tests;

public sealed class DmcDmaDuringReadRomTests
{
    public static TheoryData<string> PassingDmcDmaDuringReadRoms => new()
    {
        "dma_2007_read.nes",
        "dma_2007_write.nes",
        "dma_4016_read.nes",
        "double_2007_read.nes",
        "read_write_2007.nes"
    };

    [Theory]
    [MemberData(nameof(PassingDmcDmaDuringReadRoms))]
    public void DmcDmaDuringReadRomsPass(string romName)
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "dmc_dma_during_read4", romName);

        var result = ShellExitTestRunner.Run(NesMachine.LoadFile(romPath), 100_000_000);

        Assert.True(
            result.Passed,
            $"{romName} failed with status {result.Status}, code {result.ResultCode:X2}, after {result.InstructionsExecuted} instructions.");
    }

    private static string FindWorkspaceRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "dmc_dma_during_read4")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing dmc_dma_during_read4 test ROMs.");
    }
}
