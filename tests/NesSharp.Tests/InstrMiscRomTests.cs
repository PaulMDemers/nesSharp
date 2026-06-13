using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;

namespace NesSharp.Tests;

public sealed class InstrMiscRomTests
{
    public static TheoryData<string> PassingInstrMiscSingles => new()
    {
        "01-abs_x_wrap.nes",
        "02-branch_wrap.nes",
        "03-dummy_reads.nes",
        "04-dummy_reads_apu.nes"
    };

    [Theory]
    [MemberData(nameof(PassingInstrMiscSingles))]
    public void InstrMiscSinglesPass(string romName)
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "instr_misc", "rom_singles", romName);

        var result = BlarggTestRunner.Run(NesMachine.LoadFile(romPath), 100_000_000);

        Assert.True(
            result.Passed,
            $"{romName} failed with status {result.Status}, code {result.ResultCode:X2}, after {result.InstructionsExecuted} instructions.{Environment.NewLine}{result.Output}");
    }

    [Fact]
    public void AggregateInstrMiscRomPasses()
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "instr_misc", "instr_misc.nes");

        var result = BlarggTestRunner.Run(NesMachine.LoadFile(romPath), 100_000_000);

        Assert.True(
            result.Passed,
            $"instr_misc.nes failed with status {result.Status}, code {result.ResultCode:X2}, after {result.InstructionsExecuted} instructions.{Environment.NewLine}{result.Output}");
    }

    private static string FindWorkspaceRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "instr_misc", "rom_singles")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing instr_misc/rom_singles.");
    }
}
