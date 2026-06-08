using System.Globalization;
using System.Text.RegularExpressions;
using NesSharp.Core.Cartridge;
using NesSharp.Core.Cpu;
using NesSharp.Core.Memory;

namespace NesSharp.Tests;

public sealed partial class NestestTraceTests
{
    [Fact]
    public void CpuMatchesNestestReferenceLog()
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "other", "nestest.nes");
        var logPath = Path.Combine(root, "test-roms", "nes-test-roms", "other", "nestest.log");
        var cartridge = INesRomLoader.LoadFile(romPath);
        var cpu = new Cpu6502(new CpuBus(cartridge));
        cpu.Reset();
        cpu.SetProgramCounter(0xC000);
        cpu.SetCycles(7);

        var lineNumber = 0;
        foreach (var line in File.ReadLines(logPath))
        {
            lineNumber++;
            var expected = ParseLine(line);
            var actual = cpu.CaptureTraceState();

            Assert.True(
                expected.Matches(actual),
                $"Mismatch at line {lineNumber}.{Environment.NewLine}Expected: {expected}{Environment.NewLine}Actual:   {actual.ToNestestStateString()}{Environment.NewLine}Source:   {line}");

            cpu.Step();
        }

        Assert.Equal(8991, lineNumber);
    }

    private static ExpectedCpuState ParseLine(string line)
    {
        var match = NestestLineRegex().Match(line);
        Assert.True(match.Success, $"Could not parse nestest log line: {line}");

        return new ExpectedCpuState(
            ParseHexUShort(match.Groups["pc"].Value),
            ParseHexByte(match.Groups["op"].Value),
            ParseHexByte(match.Groups["a"].Value),
            ParseHexByte(match.Groups["x"].Value),
            ParseHexByte(match.Groups["y"].Value),
            ParseHexByte(match.Groups["p"].Value),
            ParseHexByte(match.Groups["sp"].Value),
            ulong.Parse(match.Groups["cyc"].Value, CultureInfo.InvariantCulture));
    }

    private static string FindWorkspaceRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "other", "nestest.log")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing nestest.log.");
    }

    private static byte ParseHexByte(string value) => byte.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static ushort ParseHexUShort(string value) => ushort.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<pc>[0-9A-F]{4})\s+(?<op>[0-9A-F]{2}).*A:(?<a>[0-9A-F]{2}) X:(?<x>[0-9A-F]{2}) Y:(?<y>[0-9A-F]{2}) P:(?<p>[0-9A-F]{2}) SP:(?<sp>[0-9A-F]{2}).*CYC:\s*(?<cyc>\d+)$")]
    private static partial Regex NestestLineRegex();

    private sealed record ExpectedCpuState(
        ushort ProgramCounter,
        byte Opcode,
        byte A,
        byte X,
        byte Y,
        byte Status,
        byte StackPointer,
        ulong Cycles)
    {
        public bool Matches(CpuTraceState state)
        {
            return ProgramCounter == state.ProgramCounter &&
                Opcode == state.Opcode &&
                A == state.A &&
                X == state.X &&
                Y == state.Y &&
                Status == state.Status &&
                StackPointer == state.StackPointer &&
                Cycles == state.Cycles;
        }

        public override string ToString()
        {
            return $"PC:{ProgramCounter:X4} OP:{Opcode:X2} A:{A:X2} X:{X:X2} Y:{Y:X2} P:{Status:X2} SP:{StackPointer:X2} CYC:{Cycles}";
        }
    }
}

