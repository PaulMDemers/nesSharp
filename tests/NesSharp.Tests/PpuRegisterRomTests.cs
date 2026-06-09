using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;

namespace NesSharp.Tests;

public sealed class PpuRegisterRomTests
{
    public static TheoryData<string, string> PassingPpuRegisterRoms => new()
    {
        { "ppu_open_bus", "ppu_open_bus.nes" },
        { "ppu_read_buffer", "test_ppu_read_buffer.nes" },
        { "oam_read", "oam_read.nes" },
        { "oam_stress", "oam_stress.nes" }
    };

    [Theory]
    [MemberData(nameof(PassingPpuRegisterRoms))]
    public void PpuRegisterRomsPass(string directoryName, string romName)
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", directoryName, romName);

        var result = BlarggTestRunner.Run(NesMachine.LoadFile(romPath), 100_000_000);

        Assert.True(
            result.Passed,
            $"{directoryName}/{romName} failed with status {result.Status}, code {result.ResultCode:X2}, after {result.InstructionsExecuted} instructions.{Environment.NewLine}{result.Output}");
    }

    private static string FindWorkspaceRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "ppu_open_bus")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing PPU register test ROMs.");
    }
}
