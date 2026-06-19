using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using NesSharp.Core.Cartridge;
using NesSharp.Core.Cpu;
using NesSharp.Core.Input;
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
        "compare-frame" => CompareFrame(args),
        _ => UnknownCommand(args[0])
    };
}
catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException or InvalidDataException or InvalidRomException or NotSupportedException)
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
    Console.WriteLine("  render-frame <rom.nes> --out frame.ppm|frame.bmp [--frames 1] [--input \"60-90:Start\"]");
    Console.WriteLine("                   Run a ROM and export the latest 256x240 framebuffer.");
    Console.WriteLine("  compare-frame <rom.nes> --reference frame.ppm|frame.bmp [--frames 1] [--input \"60-90:Start\"]");
    Console.WriteLine("                   Compare nesSharp RGB output against a 256x240 reference frame.");
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
        Console.Error.WriteLine("Usage: NesSharp.Cli render-frame <rom.nes> --out frame.ppm|frame.bmp [--frames 1] [--input \"60-90:Start\"] [--max-instructions 50000000]");
        return 1;
    }

    var outputPath = GetOption(args, "--out");
    if (string.IsNullOrWhiteSpace(outputPath))
    {
        Console.Error.WriteLine("Missing required --out <frame.ppm> option.");
        return 1;
    }

    var result = RenderFrameBuffer(args[1], args);
    if (!result.Completed)
    {
        Console.Error.WriteLine($"Timed out after {result.Instructions} instructions before reaching frame {result.TargetFrame}.");
        return 1;
    }

    FrameImage.Write(outputPath, result.Framebuffer);
    Console.WriteLine($"Wrote {Path.GetFullPath(outputPath)} after {result.Instructions} instructions, frame {result.Frame}.");
    return 0;
}

static int CompareFrame(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: NesSharp.Cli compare-frame <rom.nes> --reference frame.ppm|frame.bmp [--frames 1] [--out actual.ppm|actual.bmp] [--input \"60-90:Start\"] [--max-instructions 50000000]");
        return 1;
    }

    var referencePath = GetOption(args, "--reference");
    if (string.IsNullOrWhiteSpace(referencePath))
    {
        Console.Error.WriteLine("Missing required --reference <frame.ppm> option.");
        return 1;
    }

    var result = RenderFrameBuffer(args[1], args);
    if (!result.Completed)
    {
        Console.Error.WriteLine($"Timed out after {result.Instructions} instructions before reaching frame {result.TargetFrame}.");
        return 1;
    }

    var actualRgb = FrameImage.ToRgb(result.Framebuffer);
    var expectedRgb = FrameImage.ReadRgb(referencePath);
    if (expectedRgb.Length != actualRgb.Length)
    {
        Console.Error.WriteLine($"Reference RGB payload has {expectedRgb.Length} bytes; expected {actualRgb.Length}.");
        return 1;
    }

    var outputPath = GetOption(args, "--out");
    if (!string.IsNullOrWhiteSpace(outputPath))
    {
        FrameImage.Write(outputPath, result.Framebuffer);
        Console.WriteLine($"Wrote actual frame: {Path.GetFullPath(outputPath)}");
    }

    var diff = FrameDiff.Calculate(actualRgb, expectedRgb);
    Console.WriteLine($"Frame: {result.Frame}");
    Console.WriteLine($"Instructions: {result.Instructions}");
    Console.WriteLine($"Actual SHA256: {Convert.ToHexString(SHA256.HashData(actualRgb)).ToLowerInvariant()}");
    Console.WriteLine($"Reference SHA256: {Convert.ToHexString(SHA256.HashData(expectedRgb)).ToLowerInvariant()}");
    Console.WriteLine($"Differing pixels: {diff.DifferingPixels} / {FrameDiff.PixelCount}");
    Console.WriteLine($"Max channel delta: {diff.MaxChannelDelta}");
    Console.WriteLine($"Total absolute channel delta: {diff.TotalAbsoluteChannelDelta}");

    return diff.DifferingPixels == 0 ? 0 : 2;
}

static RenderFrameResult RenderFrameBuffer(string romPath, string[] args)
{
    var frames = GetIntOption(args, "--frames", 1);
    var maxInstructions = GetLongOption(args, "--max-instructions", 50_000_000);
    var inputScript = FrameInputScript.Parse(GetOption(args, "--input"));
    var machine = NesMachine.LoadFile(romPath);
    machine.Reset();
    var targetFrame = machine.PpuBus.Frame + (ulong)Math.Max(1, frames);

    long instructions = 0;
    while (machine.PpuBus.Frame < targetFrame && instructions < maxInstructions)
    {
        machine.Controller1.State = inputScript.GetState(machine.PpuBus.Frame);
        machine.StepInstruction();
        instructions++;
    }

    return new RenderFrameResult(
        machine.PpuBus.Framebuffer.ToArray(),
        machine.PpuBus.Frame,
        targetFrame,
        instructions,
        machine.PpuBus.Frame >= targetFrame);
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

internal static class FrameImage
{
    private const int Width = 256;
    private const int Height = 240;

    public static void Write(string path, ReadOnlySpan<byte> framebuffer)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        switch (extension)
        {
            case ".ppm":
                WritePpm(path, framebuffer);
                break;
            case ".bmp":
                WriteBmp(path, framebuffer);
                break;
            default:
                throw new InvalidDataException("Frame output must use .ppm or .bmp.");
        }
    }

    public static byte[] ReadRgb(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".ppm" => ReadPpmRgb(path),
            ".bmp" => ReadBmpRgb(path),
            _ => throw new InvalidDataException("Reference frame must use .ppm or .bmp.")
        };
    }

    public static byte[] ToRgb(ReadOnlySpan<byte> framebuffer)
    {
        var rgbFrame = new byte[framebuffer.Length * 3];
        Span<byte> rgb = stackalloc byte[3];
        for (var i = 0; i < framebuffer.Length; i++)
        {
            var color = NesSharp.Core.Ppu.NesPalette.GetRgb(framebuffer[i]);
            rgb[0] = color.R;
            rgb[1] = color.G;
            rgb[2] = color.B;
            rgb.CopyTo(rgbFrame.AsSpan(i * 3, 3));
        }

        return rgbFrame;
    }

    private static void WritePpm(string path, ReadOnlySpan<byte> framebuffer)
    {
        CreateParentDirectory(path);
        using var stream = File.Create(path);
        using var writer = new StreamWriter(stream, leaveOpen: true);
        writer.Write($"P6\n{Width} {Height}\n255\n");
        writer.Flush();

        var rgbFrame = ToRgb(framebuffer);
        stream.Write(rgbFrame);
    }

    private static byte[] ReadPpmRgb(string path)
    {
        var data = File.ReadAllBytes(path);
        var offset = 0;
        var magic = ReadToken(data, ref offset);
        var width = int.Parse(ReadToken(data, ref offset), CultureInfo.InvariantCulture);
        var height = int.Parse(ReadToken(data, ref offset), CultureInfo.InvariantCulture);
        var maxValue = int.Parse(ReadToken(data, ref offset), CultureInfo.InvariantCulture);
        if (magic != "P6" || width != Width || height != Height || maxValue != 255)
        {
            throw new InvalidDataException("Reference PPM frame must be a 256x240 binary PPM (P6) with max value 255.");
        }

        SkipSingleHeaderDelimiter(data, ref offset);
        var expectedLength = Width * Height * 3;
        if (data.Length - offset < expectedLength)
        {
            throw new InvalidDataException("Reference frame ended before the complete RGB payload.");
        }

        var rgb = new byte[expectedLength];
        data.AsSpan(offset, expectedLength).CopyTo(rgb);
        return rgb;
    }

    private static void WriteBmp(string path, ReadOnlySpan<byte> framebuffer)
    {
        CreateParentDirectory(path);
        var rgbFrame = ToRgb(framebuffer);
        var rowStride = GetBmpRowStride(Width);
        var pixelDataSize = rowStride * Height;
        var fileSize = 14 + 40 + pixelDataSize;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write(0);
        writer.Write(14 + 40);
        writer.Write(40);
        writer.Write(Width);
        writer.Write(Height);
        writer.Write((short)1);
        writer.Write((short)24);
        writer.Write(0);
        writer.Write(pixelDataSize);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);

        Span<byte> row = stackalloc byte[rowStride];
        for (var y = Height - 1; y >= 0; y--)
        {
            row.Clear();
            for (var x = 0; x < Width; x++)
            {
                var source = (y * Width + x) * 3;
                var target = x * 3;
                row[target] = rgbFrame[source + 2];
                row[target + 1] = rgbFrame[source + 1];
                row[target + 2] = rgbFrame[source];
            }

            stream.Write(row);
        }
    }

    private static byte[] ReadBmpRgb(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 54 || data[0] != 'B' || data[1] != 'M')
        {
            throw new InvalidDataException("Reference BMP frame must be an uncompressed 24-bit BMP.");
        }

        var pixelOffset = BitConverter.ToInt32(data, 10);
        var dibHeaderSize = BitConverter.ToInt32(data, 14);
        var width = BitConverter.ToInt32(data, 18);
        var signedHeight = BitConverter.ToInt32(data, 22);
        var planes = BitConverter.ToInt16(data, 26);
        var bitsPerPixel = BitConverter.ToInt16(data, 28);
        var compression = BitConverter.ToInt32(data, 30);
        if (dibHeaderSize < 40 ||
            width != Width ||
            Math.Abs(signedHeight) != Height ||
            planes != 1 ||
            bitsPerPixel != 24 ||
            compression != 0)
        {
            throw new InvalidDataException("Reference BMP frame must be 256x240, uncompressed, and 24-bit.");
        }

        var rowStride = GetBmpRowStride(Width);
        var requiredLength = pixelOffset + rowStride * Height;
        if (pixelOffset < 0 || requiredLength > data.Length)
        {
            throw new InvalidDataException("Reference BMP frame ended before the complete RGB payload.");
        }

        var topDown = signedHeight < 0;
        var rgb = new byte[Width * Height * 3];
        for (var y = 0; y < Height; y++)
        {
            var sourceY = topDown ? y : Height - 1 - y;
            var sourceRow = pixelOffset + sourceY * rowStride;
            for (var x = 0; x < Width; x++)
            {
                var source = sourceRow + x * 3;
                var target = (y * Width + x) * 3;
                rgb[target] = data[source + 2];
                rgb[target + 1] = data[source + 1];
                rgb[target + 2] = data[source];
            }
        }

        return rgb;
    }

    private static int GetBmpRowStride(int width) => ((width * 3) + 3) & ~3;

    private static void CreateParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string ReadToken(byte[] data, ref int offset)
    {
        SkipWhitespaceAndComments(data, ref offset);
        var start = offset;
        while (offset < data.Length && !char.IsWhiteSpace((char)data[offset]))
        {
            offset++;
        }

        if (offset == start)
        {
            throw new InvalidDataException("Invalid PPM header.");
        }

        return System.Text.Encoding.ASCII.GetString(data, start, offset - start);
    }

    private static void SkipWhitespaceAndComments(byte[] data, ref int offset)
    {
        while (offset < data.Length)
        {
            if (char.IsWhiteSpace((char)data[offset]))
            {
                offset++;
                continue;
            }

            if (data[offset] != '#')
            {
                return;
            }

            while (offset < data.Length && data[offset] is not (byte)'\n')
            {
                offset++;
            }
        }
    }

    private static void SkipSingleHeaderDelimiter(byte[] data, ref int offset)
    {
        if (offset >= data.Length || !char.IsWhiteSpace((char)data[offset]))
        {
            throw new InvalidDataException("Invalid PPM header delimiter.");
        }

        offset++;
    }
}

internal static class FrameDiff
{
    public const int PixelCount = 256 * 240;

    public static FrameDiffResult Calculate(ReadOnlySpan<byte> actualRgb, ReadOnlySpan<byte> expectedRgb)
    {
        var differingPixels = 0;
        var maxChannelDelta = 0;
        long totalAbsoluteChannelDelta = 0;

        for (var i = 0; i < actualRgb.Length; i += 3)
        {
            var pixelDiffers = false;
            for (var channel = 0; channel < 3; channel++)
            {
                var delta = Math.Abs(actualRgb[i + channel] - expectedRgb[i + channel]);
                if (delta != 0)
                {
                    pixelDiffers = true;
                }

                maxChannelDelta = Math.Max(maxChannelDelta, delta);
                totalAbsoluteChannelDelta += delta;
            }

            if (pixelDiffers)
            {
                differingPixels++;
            }
        }

        return new FrameDiffResult(differingPixels, maxChannelDelta, totalAbsoluteChannelDelta);
    }
}

internal readonly record struct RenderFrameResult(
    byte[] Framebuffer,
    ulong Frame,
    ulong TargetFrame,
    long Instructions,
    bool Completed);

internal sealed class FrameInputScript
{
    private static readonly FrameInputScript Empty = new([]);

    private readonly FrameInputRange[] ranges;

    private FrameInputScript(FrameInputRange[] ranges)
    {
        this.ranges = ranges;
    }

    public static FrameInputScript Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Empty;
        }

        var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ranges = new FrameInputRange[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            ranges[i] = ParseRange(parts[i]);
        }

        return new FrameInputScript(ranges);
    }

    public ControllerButton GetState(ulong frame)
    {
        var state = ControllerButton.None;
        foreach (var range in ranges)
        {
            if (range.Contains(frame))
            {
                state |= range.State;
            }
        }

        return state;
    }

    private static FrameInputRange ParseRange(string value)
    {
        var pieces = value.Split(':', 2, StringSplitOptions.TrimEntries);
        if (pieces.Length != 2)
        {
            throw new FormatException("Input ranges must use the form start-end:Button+Button.");
        }

        var frameRange = pieces[0].Split('-', 2, StringSplitOptions.TrimEntries);
        var start = ParseFrame(frameRange[0]);
        var end = frameRange.Length == 1 ? start : ParseFrame(frameRange[1]);
        if (end < start)
        {
            throw new FormatException("Input range end frame cannot be before the start frame.");
        }

        return new FrameInputRange(start, end, ParseButtons(pieces[1]));
    }

    private static ulong ParseFrame(string value)
    {
        return ulong.Parse(value, CultureInfo.InvariantCulture);
    }

    private static ControllerButton ParseButtons(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals("None", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            return ControllerButton.None;
        }

        var state = ControllerButton.None;
        var buttons = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var button in buttons)
        {
            state |= Enum.Parse<ControllerButton>(button, ignoreCase: true);
        }

        return state;
    }
}

internal readonly record struct FrameInputRange(ulong StartFrame, ulong EndFrame, ControllerButton State)
{
    public bool Contains(ulong frame) => frame >= StartFrame && frame <= EndFrame;
}

internal readonly record struct FrameDiffResult(
    int DifferingPixels,
    int MaxChannelDelta,
    long TotalAbsoluteChannelDelta);
