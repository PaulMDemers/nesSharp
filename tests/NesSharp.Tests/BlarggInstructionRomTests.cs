using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;

namespace NesSharp.Tests;

public sealed class BlarggInstructionRomTests
{
    [Fact]
    public void InstrTestV5SinglesPass()
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var singlesDirectory = Path.Combine(root, "test-roms", "nes-test-roms", "instr_test-v5", "rom_singles");
        var roms = Directory.GetFiles(singlesDirectory, "*.nes").Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(16, roms.Length);

        foreach (var rom in roms)
        {
            var result = BlarggTestRunner.Run(NesMachine.LoadFile(rom), 50_000_000);

            Assert.True(
                result.Passed,
                $"{Path.GetFileName(rom)} failed with status {result.Status}, code {result.ResultCode:X2}, after {result.InstructionsExecuted} instructions.{Environment.NewLine}{result.Output}");
        }
    }

    [Theory]
    [InlineData("official_only.nes")]
    [InlineData("all_instrs.nes")]
    public void InstrTestV5AggregateRomsPass(string romName)
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "instr_test-v5", romName);

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
            if (Directory.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "instr_test-v5", "rom_singles")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing instr_test-v5/rom_singles.");
    }
}
