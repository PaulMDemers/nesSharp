using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;

namespace NesSharp.Tests;

public sealed class LegacyPpuRomTests
{
    public static TheoryData<string> PassingLegacyPpuRoms => new()
    {
        "palette_ram.nes",
        "sprite_ram.nes",
        "vram_access.nes"
    };

    [Theory]
    [MemberData(nameof(PassingLegacyPpuRoms))]
    public void LegacyPpuRomsPass(string romName)
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "blargg_ppu_tests_2005.09.15b", romName);

        var result = LegacyPpuTestRunner.Run(NesMachine.LoadFile(romPath), 100_000_000);

        Assert.True(
            result.Passed,
            $"{romName} failed with status {result.Status}, code {result.ResultCode:X2}, after {result.InstructionsExecuted} instructions.");
    }

    private static string FindWorkspaceRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "blargg_ppu_tests_2005.09.15b")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing legacy PPU test ROMs.");
    }
}
