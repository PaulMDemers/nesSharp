using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;

namespace NesSharp.Tests;

public sealed class PpuSpriteRomTests
{
    public static TheoryData<string, string> PassingSpriteRoms => new()
    {
        { "sprite_hit_tests_2005.10.05", "01.basics.nes" },
        { "sprite_hit_tests_2005.10.05", "02.alignment.nes" },
        { "sprite_hit_tests_2005.10.05", "03.corners.nes" },
        { "sprite_hit_tests_2005.10.05", "04.flip.nes" },
        { "sprite_hit_tests_2005.10.05", "05.left_clip.nes" },
        { "sprite_hit_tests_2005.10.05", "06.right_edge.nes" },
        { "sprite_hit_tests_2005.10.05", "07.screen_bottom.nes" },
        { "sprite_hit_tests_2005.10.05", "08.double_height.nes" },
        { "sprite_hit_tests_2005.10.05", "09.timing_basics.nes" },
        { "sprite_hit_tests_2005.10.05", "10.timing_order.nes" },
        { "sprite_hit_tests_2005.10.05", "11.edge_timing.nes" },
        { "sprite_overflow_tests", "1.Basics.nes" },
        { "sprite_overflow_tests", "2.Details.nes" }
    };

    [Theory]
    [MemberData(nameof(PassingSpriteRoms))]
    public void SpriteRomsPass(string directoryName, string romName)
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", directoryName, romName);

        var result = SpriteResultTestRunner.Run(NesMachine.LoadFile(romPath), 100_000_000);

        Assert.True(
            result.Passed,
            $"{directoryName}/{romName} failed with status {result.Status}, code {result.ResultCode:X2}, after {result.InstructionsExecuted} instructions.");
    }

    private static string FindWorkspaceRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "sprite_hit_tests_2005.10.05")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing sprite test ROMs.");
    }
}
