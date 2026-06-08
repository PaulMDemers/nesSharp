using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;

namespace NesSharp.Tests;

public sealed class PpuVblankRomTests
{
    public static TheoryData<string> PassingPpuVblankNmiRoms => new()
    {
        "01-vbl_basics.nes",
        "03-vbl_clear_time.nes",
        "04-nmi_control.nes",
        "09-even_odd_frames.nes"
    };

    [Theory]
    [MemberData(nameof(PassingPpuVblankNmiRoms))]
    public void PpuVblankNmiBasicsPass(string romName)
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "ppu_vbl_nmi", "rom_singles", romName);

        var result = BlarggTestRunner.Run(NesMachine.LoadFile(romPath), 50_000_000);

        Assert.True(
            result.Passed,
            $"{romName} failed with status {result.Status}, code {result.ResultCode:X2}, after {result.InstructionsExecuted} instructions.{Environment.NewLine}{result.Output}");
    }

    private static string FindWorkspaceRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "ppu_vbl_nmi", "rom_singles")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing ppu_vbl_nmi/rom_singles.");
    }
}

