using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;

namespace NesSharp.Tests;

public sealed class VblNmiTimingRomTests
{
    public static TheoryData<string> PassingVblNmiTimingRoms => new()
    {
        "1.frame_basics.nes",
        "2.vbl_timing.nes",
        "3.even_odd_frames.nes",
        "4.vbl_clear_timing.nes",
        "5.nmi_suppression.nes",
        "6.nmi_disable.nes",
        "7.nmi_timing.nes"
    };

    [Theory]
    [MemberData(nameof(PassingVblNmiTimingRoms))]
    public void VblNmiTimingRomsPass(string romName)
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "vbl_nmi_timing", romName);

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
            if (Directory.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "vbl_nmi_timing")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing vbl_nmi_timing.");
    }
}

