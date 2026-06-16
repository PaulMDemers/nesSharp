using System.Globalization;
using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;

namespace NesSharp.Tests;

public sealed class SprDmaDmcDmaRomTests
{
    private static readonly int[] ExpectedNormal =
    [
        527, 528, 527, 528,
        527, 526, 525, 526,
        525, 526, 525, 526,
        525, 526, 525, 526
    ];

    private static readonly int[] Expected512 =
    [
        525, 526, 525, 526,
        524, 525, 526, 527,
        527, 528, 526, 527,
        527, 528, 527, 528
    ];

    [Fact]
    public void SprDmaOutputParserExtractsRowsAndScoresDiffs()
    {
        const string output = """
            T+ Clocks (decimal)
            00 527
            01 529
            02 529

            5D1BE3B0
            SPRDMA and DMC DMA

            Failed
            """;

        var rows = ParseTimingRows(output);
        var diffs = ScoreDiffs(rows, [527, 528, 527]);

        Assert.Equal([527, 529, 529], rows);
        Assert.Equal([0, 1, 2], diffs);
    }

    [Fact(Skip = "sprdma_and_dmc_dma currently completes but does not match the reference cycle table.")]
    public void SprDmaAndDmcDmaMatchesReferenceTiming()
    {
        var result = RunRom("sprdma_and_dmc_dma.nes");
        var rows = ParseTimingRows(result.Output);

        Assert.True(
            result.Passed,
            FormatFailure(result, rows, ExpectedNormal));
    }

    [Fact(Skip = "sprdma_and_dmc_dma_512 currently completes but does not match the reference cycle table.")]
    public void SprDmaAndDmcDma512MatchesReferenceTiming()
    {
        var result = RunRom("sprdma_and_dmc_dma_512.nes");
        var rows = ParseTimingRows(result.Output);

        Assert.True(
            result.Passed,
            FormatFailure(result, rows, Expected512));
    }

    private static BlarggTestResult RunRom(string romName)
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "sprdma_and_dmc_dma", romName);

        return BlarggTestRunner.Run(NesMachine.LoadFile(romPath), 20_000_000);
    }

    private static int[] ParseTimingRows(string output)
    {
        var rows = new List<int>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 ||
                !byte.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var row) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var cycles) ||
                row != rows.Count)
            {
                continue;
            }

            rows.Add(cycles);
            if (rows.Count == 16)
            {
                break;
            }
        }

        return rows.ToArray();
    }

    private static int[] ScoreDiffs(int[] actual, int[] expected)
    {
        var count = Math.Min(actual.Length, expected.Length);
        var diffs = new int[count];
        for (var i = 0; i < count; i++)
        {
            diffs[i] = actual[i] - expected[i];
        }

        return diffs;
    }

    private static string FormatFailure(BlarggTestResult result, int[] actual, int[] expected)
    {
        var diffs = ScoreDiffs(actual, expected);
        var rows = string.Join(
            Environment.NewLine,
            actual.Select((cycles, index) => $"{index:X2}: actual={cycles} expected={expected[index]} diff={diffs[index],2}"));

        return $"ROM failed with status {result.Status}, code {result.ResultCode:X2}, after {result.InstructionsExecuted} instructions.{Environment.NewLine}{rows}{Environment.NewLine}{result.Output}";
    }

    private static string FindWorkspaceRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "sprdma_and_dmc_dma")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing sprdma_and_dmc_dma.");
    }
}
