using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;

namespace NesSharp.Tests;

public sealed class Mmc3IrqRomTests
{
    public static TheoryData<string> PassingMmc3IrqRoms => new()
    {
        "1.Clocking.nes",
        "2.Details.nes",
        "3.A12_clocking.nes",
        "4.Scanline_timing.nes",
        "6.MMC3_rev_B.nes"
    };

    [Theory]
    [MemberData(nameof(PassingMmc3IrqRoms))]
    public void Mmc3IrqRomsPass(string romName)
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "mmc3_irq_tests", romName);

        var result = SpriteResultTestRunner.Run(NesMachine.LoadFile(romPath), 100_000_000);

        Assert.True(
            result.Passed,
            $"{romName} failed with status {result.Status}, code {result.ResultCode:X2}, after {result.InstructionsExecuted} instructions.");
    }

    private static string FindWorkspaceRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "mmc3_irq_tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing mmc3_irq_tests ROMs.");
    }
}
