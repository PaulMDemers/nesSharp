using System.Globalization;
using System.Text.RegularExpressions;
using NesSharp.Core.Cartridge;
using NesSharp.Core.Cpu;
using NesSharp.Core.Memory;
using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return 0;
}

try
{
    return args[0] switch
    {
        "info" => PrintRomInfo(args),
        "trace" => TraceRom(args),
        "nestest" => RunNestestDiff(args),
        "test-rom" => RunTestRom(args),
        "shell-test-rom" => RunShellTestRom(args),
        "render-frame" => RenderFrame(args),
        _ => UnknownCommand(args[0])
    };
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidRomException or NotSupportedException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int PrintRomInfo(string[] args)
{
    if (args.Length != 2)
    {
        Console.Error.WriteLine("Usage: NesSharp.Cli info <rom.nes>");
        return 1;
    }

    var cartridge = INesRomLoader.LoadFile(args[1]);
    var header = cartridge.Header;

    Console.WriteLine($"Path: {Path.GetFullPath(args[1])}");
    Console.WriteLine($"Format: {header.Format}");
    Console.WriteLine($"Mapper: {header.MapperNumber}");
    Console.WriteLine($"Mirroring: {header.MirroringMode}");
    Console.WriteLine($"PRG ROM: {header.PrgRomBanks} x 16 KB ({header.PrgRomSize} bytes)");
    Console.WriteLine($"CHR: {(header.UsesChrRam ? "RAM" : "ROM")} {Math.Max(header.ChrRomSize, cartridge.ChrMemory.Length)} bytes");
    Console.WriteLine($"PRG RAM: {header.PrgRamSize} bytes");
    Console.WriteLine($"Battery RAM: {header.HasBatteryBackedRam}");
    Console.WriteLine($"Trainer: {header.HasTrainer}");
    Console.WriteLine($"Four-screen VRAM: {header.HasFourScreenVram}");

    return 0;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("nesSharp CLI");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  info <rom.nes>    Parse a ROM and print cartridge metadata.");
    Console.WriteLine("  trace <rom.nes> [--start C000] [--count 20]");
    Console.WriteLine("                   Print compact CPU state trace lines.");
    Console.WriteLine("  nestest <rom.nes> <nestest.log>");
    Console.WriteLine("                   Compare CPU execution against Kevin Horton's nestest log.");
    Console.WriteLine("  test-rom <rom.nes> [--max-instructions 50000000]");
    Console.WriteLine("                   Run a blargg-style ROM and report $6000 output.");
    Console.WriteLine("  shell-test-rom <rom.nes> [--max-instructions 50000000]");
    Console.WriteLine("                   Run a shell-exit ROM and report the exit accumulator.");
    Console.WriteLine("  render-frame <rom.nes> --out frame.ppm [--frames 1] [--max-instructions 50000000]");
    Console.WriteLine("                   Run a ROM and export the latest 256x240 framebuffer as PPM.");
}

static int RunTestRom(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: NesSharp.Cli test-rom <rom.nes> [--max-instructions 50000000]");
        return 1;
    }

    var maxInstructions = GetLongOption(args, "--max-instructions", 50_000_000);
    var machine = NesMachine.LoadFile(args[1]);
    var result = BlarggTestRunner.Run(machine, maxInstructions);

    Console.WriteLine($"Status: {result.Status}");
    Console.WriteLine($"Result code: {(result.ResultCode is null ? "<none>" : $"${result.ResultCode:X2}")}");
    Console.WriteLine($"Instructions: {result.InstructionsExecuted}");
    Console.WriteLine($"CPU cycles: {result.CpuCycles}");

    if (!string.IsNullOrWhiteSpace(result.Output))
    {
        Console.WriteLine();
        Console.WriteLine(result.Output.TrimEnd());
    }

    return result.Passed ? 0 : 1;
}

static int RunShellTestRom(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: NesSharp.Cli shell-test-rom <rom.nes> [--max-instructions 50000000]");
        return 1;
    }

    var maxInstructions = GetLongOption(args, "--max-instructions", 50_000_000);
    var result = ShellExitTestRunner.Run(NesMachine.LoadFile(args[1]), maxInstructions);

    Console.WriteLine($"Status: {result.Status}");
    Console.WriteLine($"Result code: {(result.ResultCode is null ? "<none>" : $"${result.ResultCode:X2}")}");
    Console.WriteLine($"Instructions: {result.InstructionsExecuted}");
    Console.WriteLine($"CPU cycles: {result.CpuCycles}");

    return result.Passed ? 0 : 1;
}

static int RenderFrame(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: NesSharp.Cli render-frame <rom.nes> --out frame.ppm [--frames 1] [--max-instructions 50000000]");
        return 1;
    }

    var outputPath = GetOption(args, "--out");
    if (string.IsNullOrWhiteSpace(outputPath))
    {
        Console.Error.WriteLine("Missing required --out <frame.ppm> option.");
        return 1;
    }

    var frames = GetIntOption(args, "--frames", 1);
    var maxInstructions = GetLongOption(args, "--max-instructions", 50_000_000);
    var machine = NesMachine.LoadFile(args[1]);
    machine.Reset();
    var targetFrame = machine.PpuBus.Frame + (ulong)Math.Max(1, frames);

    long instructions = 0;
    while (machine.PpuBus.Frame < targetFrame && instructions < maxInstructions)
    {
        machine.StepInstruction();
        instructions++;
    }

    if (machine.PpuBus.Frame < targetFrame)
    {
        Console.Error.WriteLine($"Timed out after {instructions} instructions before reaching frame {targetFrame}.");
        return 1;
    }

    PpmWriter.Write(outputPath, machine.PpuBus.Framebuffer);
    Console.WriteLine($"Wrote {Path.GetFullPath(outputPath)} after {instructions} instructions, frame {machine.PpuBus.Frame}.");
    return 0;
}

static int TraceRom(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: NesSharp.Cli trace <rom.nes> [--start C000] [--count 20]");
        return 1;
    }

    var start = GetUShortOption(args, "--start", 0xC000);
    var count = GetIntOption(args, "--count", 20);
    var cpu = CreateCpu(args[1], start);

    for (var i = 0; i < count; i++)
    {
        var state = cpu.CaptureTraceState();
        Console.WriteLine(FormatTraceLine(state));
        cpu.Step();
    }

    return 0;
}

static int RunNestestDiff(string[] args)
{
    if (args.Length != 3)
    {
        Console.Error.WriteLine("Usage: NesSharp.Cli nestest <rom.nes> <nestest.log>");
        return 1;
    }

    var cpu = CreateCpu(args[1], 0xC000);
    var lineNumber = 0;

    foreach (var line in File.ReadLines(args[2]))
    {
        lineNumber++;
        var expected = NestestLogState.Parse(line);
        var actual = cpu.CaptureTraceState();

        if (!expected.Matches(actual))
        {
            Console.Error.WriteLine($"Mismatch at line {lineNumber}:");
            Console.Error.WriteLine($"Expected: {expected}");
            Console.Error.WriteLine($"Actual:   {actual.ToNestestStateString()}");
            Console.Error.WriteLine($"Source:   {line}");
            return 1;
        }

        try
        {
            cpu.Step();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Execution failed after matching line {lineNumber}: {ex.Message}");
            return 1;
        }
    }

    Console.WriteLine($"nestest matched {lineNumber} instruction states.");
    return 0;
}

static Cpu6502 CreateCpu(string romPath, ushort start)
{
    var cartridge = INesRomLoader.LoadFile(romPath);
    var bus = new CpuBus(cartridge);
    var cpu = new Cpu6502(bus);
    cpu.Reset();
    cpu.SetProgramCounter(start);
    cpu.SetCycles(7);
    return cpu;
}

static string FormatTraceLine(CpuTraceState state)
{
    var bytes = state.InstructionLength switch
    {
        1 => $"{state.Opcode:X2}",
        2 => $"{state.Opcode:X2} {state.Operand1:X2}",
        _ => $"{state.Opcode:X2} {state.Operand1:X2} {state.Operand2:X2}"
    };

    return $"{state.ProgramCounter:X4}  {bytes,-8} A:{state.A:X2} X:{state.X:X2} Y:{state.Y:X2} P:{state.Status:X2} SP:{state.StackPointer:X2} PPU:{state.PpuScanline,3},{state.PpuDot,3} CYC:{state.Cycles}";
}

static ushort GetUShortOption(string[] args, string name, ushort defaultValue)
{
    var value = GetOption(args, name);
    return value is null ? defaultValue : ushort.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}

static int GetIntOption(string[] args, string name, int defaultValue)
{
    var value = GetOption(args, name);
    return value is null ? defaultValue : int.Parse(value, CultureInfo.InvariantCulture);
}

static long GetLongOption(string[] args, string name, long defaultValue)
{
    var value = GetOption(args, name);
    return value is null ? defaultValue : long.Parse(value, CultureInfo.InvariantCulture);
}

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == name)
        {
            return args[i + 1];
        }
    }

    return null;
}

internal sealed partial record NestestLogState(
    ushort ProgramCounter,
    byte Opcode,
    byte A,
    byte X,
    byte Y,
    byte Status,
    byte StackPointer,
    ulong Cycles)
{
    public static NestestLogState Parse(string line)
    {
        var match = NestestLineRegex().Match(line);
        if (!match.Success)
        {
            throw new FormatException($"Could not parse nestest log line: {line}");
        }

        return new NestestLogState(
            ParseHexUShort(match.Groups["pc"].Value),
            ParseHexByte(match.Groups["op"].Value),
            ParseHexByte(match.Groups["a"].Value),
            ParseHexByte(match.Groups["x"].Value),
            ParseHexByte(match.Groups["y"].Value),
            ParseHexByte(match.Groups["p"].Value),
            ParseHexByte(match.Groups["sp"].Value),
            ulong.Parse(match.Groups["cyc"].Value, CultureInfo.InvariantCulture));
    }

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

    private static byte ParseHexByte(string value) => byte.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static ushort ParseHexUShort(string value) => ushort.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<pc>[0-9A-F]{4})\s+(?<op>[0-9A-F]{2}).*A:(?<a>[0-9A-F]{2}) X:(?<x>[0-9A-F]{2}) Y:(?<y>[0-9A-F]{2}) P:(?<p>[0-9A-F]{2}) SP:(?<sp>[0-9A-F]{2}).*CYC:\s*(?<cyc>\d+)$")]
    private static partial Regex NestestLineRegex();
}

internal static class PpmWriter
{
    public static void Write(string path, ReadOnlySpan<byte> framebuffer)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(path);
        using var writer = new StreamWriter(stream, leaveOpen: true);
        writer.Write("P6\n256 240\n255\n");
        writer.Flush();

        Span<byte> rgb = stackalloc byte[3];
        foreach (var paletteIndex in framebuffer)
        {
            var color = NesSharp.Core.Ppu.NesPalette.GetRgb(paletteIndex);
            rgb[0] = color.R;
            rgb[1] = color.G;
            rgb[2] = color.B;
            stream.Write(rgb);
        }
    }
}
