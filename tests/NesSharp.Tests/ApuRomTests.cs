using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;

namespace NesSharp.Tests;

public sealed class ApuRomTests
{
    public static TheoryData<string> PassingApuRoms => new()
    {
        "1-len_ctr.nes",
        "2-len_table.nes",
        "3-irq_flag.nes",
        "4-jitter.nes",
        "5-len_timing.nes",
        "7-dmc_basics.nes",
        "8-dmc_rates.nes"
    };

    [Theory]
    [MemberData(nameof(PassingApuRoms))]
    public void ApuRomsPass(string romName)
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "apu_test", "rom_singles", romName);

        var result = BlarggTestRunner.Run(NesMachine.LoadFile(romPath), 20_000_000);

        Assert.True(
            result.Passed,
            $"{romName} failed with status {result.Status}, code {result.ResultCode:X2}, after {result.InstructionsExecuted} instructions.{Environment.NewLine}{result.Output}");
    }

    private static string FindWorkspaceRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "apu_test", "rom_singles")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing apu_test/rom_singles.");
    }
}
