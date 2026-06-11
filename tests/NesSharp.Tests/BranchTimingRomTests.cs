using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;

namespace NesSharp.Tests;

public sealed class BranchTimingRomTests
{
    public static TheoryData<string> PassingBranchTimingRoms => new()
    {
        "1.Branch_Basics.nes",
        "2.Backward_Branch.nes",
        "3.Forward_Branch.nes"
    };

    [Theory]
    [MemberData(nameof(PassingBranchTimingRoms))]
    public void BranchTimingRomsPass(string romName)
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "branch_timing_tests", romName);

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
            if (Directory.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "branch_timing_tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing branch_timing_tests.");
    }
}

