using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;

namespace NesSharp.Tests;

public sealed class ApuResetRomTests
{
    public static TheoryData<string> PassingApuResetRoms => new()
    {
        "4015_cleared.nes",
        "4017_timing.nes",
        "irq_flag_cleared.nes",
        "works_immediately.nes"
    };

    [Theory]
    [MemberData(nameof(PassingApuResetRoms))]
    public void ApuResetRomsPass(string romName)
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "apu_reset", romName);

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
            if (Directory.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "apu_reset")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing apu_reset.");
    }
}
