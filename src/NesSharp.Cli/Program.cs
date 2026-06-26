using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using NesSharp.Core.Cartridge;
using NesSharp.Core.Cpu;
using NesSharp.Core.Input;
using NesSharp.Core.Memory;
using NesSharp.Core.Ppu;
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
        "render-sequence" => RenderSequence(args),
        "compare-frame" => CompareFrame(args),
        "scan-frame-match" => ScanFrameMatch(args),
        "sample-frames" => SampleFrames(args),
        "diagnose-frame" => DiagnoseFrame(args),
        "trace-writes" => TraceWrites(args),
        "trace-dma" => TraceDma(args),
        "sprdma-report" => ReportSprDma(args),
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
    Console.WriteLine("  render-sequence <rom.nes> --out-dir frames --start-frame 1 --end-frame 300 [--step 1] [--format bmp]");
    Console.WriteLine("                   Run a ROM once and export a numbered frame sequence.");
    Console.WriteLine("  compare-frame <rom.nes> --reference frame.ppm|frame.bmp [--frames 1] [--input \"60-90:Start\"]");
    Console.WriteLine("                   Compare nesSharp RGB output against a 256x240 reference frame.");
    Console.WriteLine("  scan-frame-match <rom.nes> --reference frame.ppm|frame.bmp --start-frame 1 --end-frame 300");
    Console.WriteLine("                   Compare a reference against each frame in a nesSharp frame range.");
    Console.WriteLine("  sample-frames <rom.nes> --end-frame 1200 [--start-frame 1] [--step 60] [--input \"60-90:Start\"]");
    Console.WriteLine("                   Print framebuffer hashes and palette histograms at frame intervals.");
    Console.WriteLine("  diagnose-frame <rom.nes> [--frames 1] [--input \"60-90:Start\"] [--dump-state-dir dir]");
    Console.WriteLine("                   Print PPU and mapper state after running to a frame, optionally dumping binary PPU state.");
    Console.WriteLine("  trace-writes <rom.nes> [--frames 1] [--start-frame N] [--scanline-start N] [--scanline-end N] [--include-controller]");
    Console.WriteLine("                   Print PPU register and mapper writes with PPU timing.");
    Console.WriteLine("  trace-dma <rom.nes> [--max-instructions 50000000] [--max-events 200] [--include-status]");
    Console.WriteLine("                   Print OAM/DMC DMA events with current CPU instruction PC.");
    Console.WriteLine("  sprdma-report <rom.nes> [--max-instructions 20000000]");
    Console.WriteLine("                   Print compact row timing for sprdma_and_dmc_dma test ROMs.");
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

static int ScanFrameMatch(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: NesSharp.Cli scan-frame-match <rom.nes> --reference frame.ppm|frame.bmp --start-frame 1 --end-frame 300 [--input \"60-90:Start\"] [--max-instructions 50000000]");
        return 1;
    }

    var referencePath = GetOption(args, "--reference");
    if (string.IsNullOrWhiteSpace(referencePath))
    {
        Console.Error.WriteLine("Missing required --reference <frame.ppm|frame.bmp> option.");
        return 1;
    }

    var startFrame = GetIntOption(args, "--start-frame", 1);
    var endFrame = GetIntOption(args, "--end-frame", startFrame);
    if (startFrame < 1 || endFrame < startFrame)
    {
        Console.Error.WriteLine("Frame range must satisfy 1 <= start-frame <= end-frame.");
        return 1;
    }

    var expectedRgb = FrameImage.ReadRgb(referencePath);
    var maxInstructions = GetLongOption(args, "--max-instructions", 50_000_000);
    var inputScript = FrameInputScript.Parse(GetOption(args, "--input"));
    var machine = NesMachine.LoadFile(args[1]);
    machine.Reset();

    long instructions = 0;
    ulong lastScannedFrame = 0;
    FrameScanResult? best = null;
    Console.WriteLine("frame,instructions,differing_pixels,max_channel_delta,total_absolute_channel_delta");

    while (machine.PpuBus.Frame < (ulong)endFrame && instructions < maxInstructions)
    {
        machine.Controller1.State = inputScript.GetState(machine.PpuBus.Frame);
        machine.StepInstruction();
        instructions++;

        if (machine.PpuBus.Frame < (ulong)startFrame ||
            machine.PpuBus.Frame > (ulong)endFrame ||
            machine.PpuBus.Frame == lastScannedFrame)
        {
            continue;
        }

        lastScannedFrame = machine.PpuBus.Frame;
        var actualRgb = FrameImage.ToRgb(machine.PpuBus.Framebuffer);
        var diff = FrameDiff.Calculate(actualRgb, expectedRgb);
        Console.WriteLine($"{machine.PpuBus.Frame},{instructions},{diff.DifferingPixels},{diff.MaxChannelDelta},{diff.TotalAbsoluteChannelDelta}");

        var candidate = new FrameScanResult(machine.PpuBus.Frame, instructions, diff);
        if (best is null || candidate.Diff.TotalAbsoluteChannelDelta < best.Value.Diff.TotalAbsoluteChannelDelta)
        {
            best = candidate;
        }
    }

    if (machine.PpuBus.Frame < (ulong)endFrame)
    {
        Console.Error.WriteLine($"Timed out after {instructions} instructions before reaching frame {endFrame}.");
        return 1;
    }

    if (best is not null)
    {
        Console.WriteLine($"Best frame: {best.Value.Frame}");
        Console.WriteLine($"Best instructions: {best.Value.Instructions}");
        Console.WriteLine($"Best differing pixels: {best.Value.Diff.DifferingPixels} / {FrameDiff.PixelCount}");
        Console.WriteLine($"Best max channel delta: {best.Value.Diff.MaxChannelDelta}");
        Console.WriteLine($"Best total absolute channel delta: {best.Value.Diff.TotalAbsoluteChannelDelta}");
    }

    return 0;
}

static int SampleFrames(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: NesSharp.Cli sample-frames <rom.nes> --end-frame 1200 [--start-frame 1] [--step 60] [--input \"60-90:Start\"] [--max-instructions 50000000]");
        return 1;
    }

    var startFrame = GetIntOption(args, "--start-frame", 1);
    var endFrame = GetIntOption(args, "--end-frame", startFrame);
    var step = GetIntOption(args, "--step", 60);
    if (startFrame < 1 || endFrame < startFrame || step < 1)
    {
        Console.Error.WriteLine("Frame range must satisfy 1 <= start-frame <= end-frame and step >= 1.");
        return 1;
    }

    var maxInstructions = GetLongOption(args, "--max-instructions", 50_000_000);
    var inputScript = FrameInputScript.Parse(GetOption(args, "--input"));
    var machine = NesMachine.LoadFile(args[1]);
    machine.Reset();

    long instructions = 0;
    var nextSample = (ulong)startFrame;
    Console.WriteLine("frame,instructions,fb_sha256,nonzero_pixels,top_palette_indices");

    while (machine.PpuBus.Frame < (ulong)endFrame && instructions < maxInstructions)
    {
        machine.Controller1.State = inputScript.GetState(machine.PpuBus.Frame);
        machine.StepInstruction();
        instructions++;

        if (machine.PpuBus.Frame < nextSample)
        {
            continue;
        }

        PrintFrameSample(machine.PpuBus.Framebuffer, machine.PpuBus.Frame, instructions);
        while (nextSample <= machine.PpuBus.Frame)
        {
            nextSample += (ulong)step;
        }
    }

    if (machine.PpuBus.Frame < (ulong)endFrame)
    {
        Console.Error.WriteLine($"Timed out after {instructions} instructions before reaching frame {endFrame}.");
        return 1;
    }

    return 0;
}

static int RenderSequence(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: NesSharp.Cli render-sequence <rom.nes> --out-dir frames --start-frame 1 --end-frame 300 [--step 1] [--format bmp] [--input \"60-90:Start\"] [--max-instructions 50000000]");
        return 1;
    }

    var outputDirectory = GetOption(args, "--out-dir");
    if (string.IsNullOrWhiteSpace(outputDirectory))
    {
        Console.Error.WriteLine("Missing required --out-dir <directory> option.");
        return 1;
    }

    var startFrame = GetIntOption(args, "--start-frame", 1);
    var endFrame = GetIntOption(args, "--end-frame", startFrame);
    var step = GetIntOption(args, "--step", 1);
    if (startFrame < 1 || endFrame < startFrame || step < 1)
    {
        Console.Error.WriteLine("Frame range must satisfy 1 <= start-frame <= end-frame and step >= 1.");
        return 1;
    }

    var format = GetOption(args, "--format") ?? "bmp";
    if (format is not ("bmp" or "ppm"))
    {
        Console.Error.WriteLine("--format must be either bmp or ppm.");
        return 1;
    }

    var maxInstructions = GetLongOption(args, "--max-instructions", 50_000_000);
    var inputScript = FrameInputScript.Parse(GetOption(args, "--input"));
    var machine = NesMachine.LoadFile(args[1]);
    machine.Reset();

    Directory.CreateDirectory(outputDirectory);

    long instructions = 0;
    var nextFrame = (ulong)startFrame;
    var sequenceIndex = 0;
    Console.WriteLine("sequence_index,frame,instructions,path");

    while (machine.PpuBus.Frame < (ulong)endFrame && instructions < maxInstructions)
    {
        machine.Controller1.State = inputScript.GetState(machine.PpuBus.Frame);
        machine.StepInstruction();
        instructions++;

        if (machine.PpuBus.Frame < nextFrame)
        {
            continue;
        }

        var outputPath = Path.Combine(outputDirectory, $"seq_{sequenceIndex:D4}.{format}");
        FrameImage.Write(outputPath, machine.PpuBus.Framebuffer);
        Console.WriteLine($"{sequenceIndex},{machine.PpuBus.Frame},{instructions},{Path.GetFullPath(outputPath)}");
        sequenceIndex++;

        while (nextFrame <= machine.PpuBus.Frame)
        {
            nextFrame += (ulong)step;
        }
    }

    if (machine.PpuBus.Frame < (ulong)endFrame)
    {
        Console.Error.WriteLine($"Timed out after {instructions} instructions before reaching frame {endFrame}.");
        return 1;
    }

    return 0;
}

static RenderFrameResult RenderFrameBuffer(string romPath, string[] args)
{
    var result = RunMachineToFrame(romPath, args);
    return new RenderFrameResult(
        result.Machine.PpuBus.Framebuffer.ToArray(),
        result.Machine.PpuBus.Frame,
        result.TargetFrame,
        result.Instructions,
        result.Completed);
}

static int DiagnoseFrame(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: NesSharp.Cli diagnose-frame <rom.nes> [--frames 1] [--input \"60-90:Start\"] [--dump-state-dir dir] [--max-instructions 50000000]");
        return 1;
    }

    var result = RunMachineToFrame(args[1], args);
    if (!result.Completed)
    {
        Console.Error.WriteLine($"Timed out after {result.Instructions} instructions before reaching frame {result.TargetFrame}.");
        return 1;
    }

    PrintFrameDiagnosis(result);
    var dumpStateDirectory = GetOption(args, "--dump-state-dir");
    if (!string.IsNullOrWhiteSpace(dumpStateDirectory))
    {
        WriteFrameDiagnosisDump(result.Machine, dumpStateDirectory);
    }

    return 0;
}

static int TraceWrites(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: NesSharp.Cli trace-writes <rom.nes> [--frames 1] [--start-frame N] [--scanline-start N] [--scanline-end N] [--include-controller] [--input \"60-90:Start\"] [--max-instructions 50000000]");
        return 1;
    }

    var endFrame = (ulong)GetIntOption(args, "--end-frame", GetIntOption(args, "--frames", 1));
    var startFrame = (ulong)GetIntOption(args, "--start-frame", (int)endFrame);
    if (startFrame > endFrame)
    {
        Console.Error.WriteLine("--start-frame must be less than or equal to --end-frame.");
        return 1;
    }

    var scanlineStart = GetIntOption(args, "--scanline-start", int.MinValue);
    var scanlineEnd = GetIntOption(args, "--scanline-end", int.MaxValue);
    var includeController = args.Contains("--include-controller", StringComparer.Ordinal);
    var maxInstructions = GetLongOption(args, "--max-instructions", 50_000_000);
    var inputScript = FrameInputScript.Parse(GetOption(args, "--input"));
    var machine = NesMachine.LoadFile(args[1]);
    machine.Reset();
    var tracedEvents = 0;
    var currentInstructionPc = machine.Cpu.ProgramCounter;

    machine.CpuBus.ReadObserved += entry =>
    {
        if (entry.PpuFrame < startFrame ||
            entry.PpuFrame > endFrame ||
            entry.PpuScanline < scanlineStart ||
            entry.PpuScanline > scanlineEnd ||
            !ShouldTraceRead(entry.Address, includeController))
        {
            return;
        }

        tracedEvents++;
        Console.WriteLine(FormatReadTrace(entry, currentInstructionPc));
    };

    machine.CpuBus.WriteObserved += entry =>
    {
        if (entry.PpuFrame < startFrame ||
            entry.PpuFrame > endFrame ||
            entry.PpuScanline < scanlineStart ||
            entry.PpuScanline > scanlineEnd ||
            !ShouldTraceWrite(entry.Address, includeController))
        {
            return;
        }

        tracedEvents++;
        Console.WriteLine(FormatWriteTrace(entry, machine.Cartridge.Mapper, currentInstructionPc));
    };

    long instructions = 0;
    var targetFrame = endFrame + 1;
    while (machine.PpuBus.Frame < targetFrame && instructions < maxInstructions)
    {
        machine.Controller1.State = inputScript.GetState(machine.PpuBus.Frame);
        currentInstructionPc = machine.Cpu.ProgramCounter;
        machine.StepInstruction();
        instructions++;
    }

    Console.WriteLine($"Traced events: {tracedEvents}");
    Console.WriteLine($"Instructions: {instructions}");
    Console.WriteLine($"Frame reached: {machine.PpuBus.Frame} (target {targetFrame})");
    return machine.PpuBus.Frame >= targetFrame ? 0 : 1;
}

static int TraceDma(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: NesSharp.Cli trace-dma <rom.nes> [--max-instructions 50000000] [--max-events 200] [--include-status]");
        return 1;
    }

    var maxInstructions = GetLongOption(args, "--max-instructions", 50_000_000);
    var maxEvents = GetIntOption(args, "--max-events", 200);
    var includeStatus = HasOption(args, "--include-status");
    var machine = NesMachine.LoadFile(args[1]);
    machine.Reset();
    var currentInstructionPc = machine.Cpu.ProgramCounter;
    var events = 0;

    machine.CpuBus.DmaObserved += entry =>
    {
        if (events >= maxEvents)
        {
            return;
        }

        events++;
        Console.WriteLine(FormatDmaTrace(entry, currentInstructionPc, machine.Cpu.Cycles));
    };

    if (includeStatus)
    {
        machine.CpuBus.ReadObserved += entry =>
        {
            if (events >= maxEvents || entry.Address != 0x4015)
            {
                return;
            }

            events++;
            Console.WriteLine(FormatStatusTrace("read", entry.Address, entry.Value, currentInstructionPc, machine.Cpu.Cycles));
        };

        machine.CpuBus.WriteObserved += entry =>
        {
            if (events >= maxEvents || entry.Address != 0x4015)
            {
                return;
            }

            events++;
            Console.WriteLine(FormatStatusTrace("write", entry.Address, entry.Value, currentInstructionPc, machine.Cpu.Cycles));
        };
    }

    long instructions = 0;
    while (instructions < maxInstructions && events < maxEvents)
    {
        currentInstructionPc = machine.Cpu.ProgramCounter;
        machine.StepInstruction();
        instructions++;
    }

    Console.WriteLine($"Traced DMA events: {events}");
    Console.WriteLine($"Instructions: {instructions}");
    Console.WriteLine($"CPU cycles: {machine.Cpu.Cycles}");
    return events > 0 ? 0 : 1;
}

static int ReportSprDma(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: NesSharp.Cli sprdma-report <rom.nes> [--max-instructions 20000000]");
        return 1;
    }

    var maxInstructions = GetLongOption(args, "--max-instructions", 20_000_000);
    var machine = NesMachine.LoadFile(args[1]);
    machine.Reset();
    var rows = new List<SprDmaTraceRow>();
    SprDmaTraceRow? currentRow = null;
    SprDmaTraceRow? postOamRow = null;
    CpuBusDmaDebugEntry? lastDmcEntry = null;
    ulong lastDmcCycle = 0;

    machine.CpuBus.DmaObserved += entry =>
    {
        switch (entry.Kind)
        {
            case "oam-start":
                if (rows.Count >= 16)
                {
                    return;
                }

                currentRow = new SprDmaTraceRow(rows.Count)
                {
                    StartNext = entry.NextDmaCycleIsGet ? "get" : "put",
                    StartNextIsGet = entry.NextDmaCycleIsGet,
                    StartPending = entry.IsDmcPending,
                    StartReady = entry.IsDmcReady,
                    StartDmcHaltRetry = entry.DmcHaltRetry,
                    StartDmcLoadDelay = entry.DmcLoadHaltDelayCycles,
                    StartAccess = entry.CpuAccessCycles,
                    StartInstructionAccess = entry.InstructionAccessCycles
                };
                if (lastDmcEntry is not null)
                {
                    currentRow.PreOamDmcKind = lastDmcEntry.Value.Kind;
                    currentRow.PreOamDmcDetail = lastDmcEntry.Value.Detail;
                    currentRow.PreOamDmcAccess = lastDmcEntry.Value.CpuAccessCycles;
                    currentRow.PreOamDmcInstructionAccess = lastDmcEntry.Value.InstructionAccessCycles;
                    currentRow.PreOamDmcCycleDelta = machine.Cpu.Cycles - lastDmcCycle;
                }

                break;
            case "dmc-during-oam":
            case "dmc-during-oam-start-ready":
            case "dmc-during-oam-setup-ready":
                if (currentRow is not null)
                {
                    currentRow.DmcKind = entry.Kind;
                    currentRow.DmcOamIndex = entry.Detail;
                    currentRow.DmcAccess = entry.CpuAccessCycles;
                    currentRow.FirstPendingSetupCycle = entry.OamDmcFirstPendingSetupCycle;
                    currentRow.FirstReadySetupCycle = entry.OamDmcFirstReadySetupCycle;
                    currentRow.FirstPendingIndex = entry.OamDmcFirstPendingIndex;
                    currentRow.FirstReadyIndex = entry.OamDmcFirstReadyIndex;
                    currentRow.DmcHaltRetry = entry.DmcHaltRetry;
                    currentRow.DmcLoadDelay = entry.DmcLoadHaltDelayCycles;
                }

                break;
            case "oam-end":
                if (currentRow is not null)
                {
                    currentRow.OamEndAccess = entry.CpuAccessCycles;
                    rows.Add(currentRow);
                    postOamRow = currentRow;
                    currentRow = null;
                }

                break;
        }

        if (entry.Kind.StartsWith("dmc", StringComparison.Ordinal))
        {
            lastDmcEntry = entry;
            lastDmcCycle = machine.Cpu.Cycles;
        }
    };

    machine.CpuBus.ReadObserved += entry =>
    {
        if (entry.Address != 0x4015 || rows.Count >= 16)
        {
            return;
        }

        TrackStatusRead(postOamRow, entry.Value);
    };

    machine.CpuBus.WriteObserved += entry =>
    {
        if (entry.Address != 0x4015 || rows.Count >= 16)
        {
            return;
        }

        TrackStatusWrite(postOamRow);
    };

    long instructions;
    for (instructions = 0; instructions < maxInstructions; instructions++)
    {
        if (IsBlarggComplete(machine))
        {
            break;
        }

        machine.StepInstruction();
    }

    var output = ReadBlarggOutput(machine);
    var actual = ParseSprDmaTimingRows(output);
    var expected = IsSprDma512(args[1]) ? SprDmaReportData.Expected512 : SprDmaReportData.ExpectedNormal;

    Console.WriteLine("row actual expected diff start/access pending/ready retry/delay setup-p/r first-p/r dmc-index/access sched(H/D/R)|obs dmc-kind pre-dmc oam-end status-r/w status10/0");
    for (var i = 0; i < Math.Min(16, Math.Max(actual.Length, rows.Count)); i++)
    {
        var row = i < rows.Count ? rows[i] : null;
        var actualText = i < actual.Length ? actual[i].ToString(CultureInfo.InvariantCulture) : "-";
        var expectedText = i < expected.Length ? expected[i].ToString(CultureInfo.InvariantCulture) : "-";
        var diffText = i < actual.Length && i < expected.Length
            ? (actual[i] - expected[i]).ToString(CultureInfo.InvariantCulture)
            : "-";
        var dmcText = row?.DmcOamIndex is null
            ? "-"
            : $"{row.DmcOamIndex}/{row.DmcAccess}";
        var firstText = row?.FirstPendingIndex is null && row?.FirstReadyIndex is null
            ? "-"
            : $"{FormatNullableInt(row?.FirstPendingIndex)}/{FormatNullableInt(row?.FirstReadyIndex)}";
        var setupText = row?.FirstPendingSetupCycle is null && row?.FirstReadySetupCycle is null
            ? "-"
            : $"{FormatNullableInt(row?.FirstPendingSetupCycle)}/{FormatNullableInt(row?.FirstReadySetupCycle)}";
        var startText = row is null ? "-" : $"{row.StartNext}/{row.StartAccess}/{row.StartInstructionAccess}";
        var pendingText = row is null ? "-" : $"{row.StartPending}/{row.StartReady}";
        var retryText = row is null ? "-" : $"{row.StartDmcHaltRetry}/{row.StartDmcLoadDelay}:{FormatNullableBool(row.DmcHaltRetry)}/{FormatNullableInt(row.DmcLoadDelay)}";
        var kindText = row?.DmcKind ?? "-";
        var schedulerText = FormatSchedulerShadow(row);
        var preDmcText = FormatPreOamDmc(row);
        var oamEndText = row?.OamEndAccess?.ToString(CultureInfo.InvariantCulture) ?? "-";
        var statusReadWrite = row is null ? "-" : $"{row.StatusReads}/{row.StatusWrites}";
        var statusValues = row is null ? "-" : $"{row.StatusDmcActiveReads}/{row.StatusDmcInactiveReads}";

        Console.WriteLine(
            $"{i:X2} {actualText,6} {expectedText,8} {diffText,4} {startText,12} {pendingText,13} {retryText,17} {setupText,9} {firstText,9} {dmcText,16} {schedulerText,28} {kindText,-26} {preDmcText,20} {oamEndText,7} {statusReadWrite,10} {statusValues,10}");
    }

    Console.WriteLine($"Instructions: {instructions}");
    Console.WriteLine($"Status: {(IsBlarggComplete(machine) ? machine.CpuBus.ReadRaw(SprDmaReportData.BlarggStatusAddress).ToString("X2", CultureInfo.InvariantCulture) : "timeout")}");
    return actual.Length > 0 ? 0 : 1;
}

static void TrackStatusRead(SprDmaTraceRow? row, byte value)
{
    if (row is null)
    {
        return;
    }

    row.StatusReads++;
    if ((value & 0x10) != 0)
    {
        row.StatusDmcActiveReads++;
    }
    else
    {
        row.StatusDmcInactiveReads++;
    }
}

static void TrackStatusWrite(SprDmaTraceRow? row)
{
    if (row is null)
    {
        return;
    }

    row.StatusWrites++;
}

static string FormatNullableInt(int? value)
{
    return value?.ToString(CultureInfo.InvariantCulture) ?? "-";
}

static string FormatNullableBool(bool? value)
{
    return value?.ToString() ?? "-";
}

static string FormatPreOamDmc(SprDmaTraceRow? row)
{
    if (row?.PreOamDmcKind is null)
    {
        return "-";
    }

    return string.Create(
        CultureInfo.InvariantCulture,
        $"{row.PreOamDmcKind}:{FormatNullableInt(row.PreOamDmcDetail)}/{FormatNullableInt(row.PreOamDmcAccess)}/{FormatNullableInt(row.PreOamDmcInstructionAccess)}+{row.PreOamDmcCycleDelta}");
}

static string FormatSchedulerShadow(SprDmaTraceRow? row)
{
    if (row is null)
    {
        return "-";
    }

    var dmcStartCycle = EstimateSchedulerDmcStartCycle(row);
    if (dmcStartCycle is null)
    {
        return "-";
    }

    var observedDmc = row.DmcAccess is null ? "-" : (row.DmcAccess.Value - row.StartAccess).ToString(CultureInfo.InvariantCulture);
    var observedEnd = row.OamEndAccess is null ? "-" : (row.OamEndAccess.Value - row.StartAccess).ToString(CultureInfo.InvariantCulture);
    var needHalt = FormatSchedulerShadowState(row, dmcStartCycle.Value, CpuDmaDmcState.NeedHalt);
    var needDummy = FormatSchedulerShadowState(row, dmcStartCycle.Value, CpuDmaDmcState.NeedDummy);
    var ready = FormatSchedulerShadowState(row, dmcStartCycle.Value, CpuDmaDmcState.ReadyToRead);

    return string.Create(
        CultureInfo.InvariantCulture,
        $"H{needHalt} D{needDummy} R{ready}|{observedDmc}:{observedEnd}");
}

static string FormatSchedulerShadowState(SprDmaTraceRow row, int dmcStartCycle, CpuDmaDmcState dmcStartState)
{
    var schedule = CpuDmaScheduler.Simulate(new CpuDmaScheduleRequest(
        row.StartNextIsGet,
        OamByteCount: 256,
        dmcStartCycle,
        DmcStartState: dmcStartState));
    var dmcRead = schedule.Events.FirstOrDefault(e => e.Kind == CpuDmaCycleKind.DmcRead);
    if (dmcRead.Kind != CpuDmaCycleKind.DmcRead)
    {
        return "-";
    }

    return string.Create(
        CultureInfo.InvariantCulture,
        $"{dmcRead.Cycle}:{schedule.CycleCount}");
}

static int? EstimateSchedulerDmcStartCycle(SprDmaTraceRow row)
{
    if (row.DmcKind is null)
    {
        return null;
    }

    if (row.FirstReadySetupCycle is not null)
    {
        return row.FirstReadySetupCycle.Value;
    }

    if (row.FirstReadyIndex is not null)
    {
        return OamSetupCycleCount(row.StartNextIsGet) + row.FirstReadyIndex.Value * 2;
    }

    return null;
}

static int OamSetupCycleCount(bool startNextIsGet) => startNextIsGet ? 3 : 2;

static string FormatStatusTrace(string kind, ushort address, byte value, ushort pc, ulong cpuCycles)
{
    return string.Create(
        CultureInfo.InvariantCulture,
        $"PC={pc:X4} CYC={cpuCycles} STATUS={kind} addr=${address:X4} val=${value:X2}");
}

static string FormatDmaTrace(CpuBusDmaDebugEntry entry, ushort pc, ulong cpuCycles)
{
    var address = entry.Address is null ? "----" : $"${entry.Address:X4}";
    var value = entry.Value is null ? "--" : $"${entry.Value:X2}";
    var detail = entry.Detail is null ? "-" : entry.Detail.Value.ToString(CultureInfo.InvariantCulture);
    var setup = entry.OamDmcFirstPendingSetupCycle is null && entry.OamDmcFirstReadySetupCycle is null
        ? "-"
        : $"{FormatNullableInt(entry.OamDmcFirstPendingSetupCycle)}/{FormatNullableInt(entry.OamDmcFirstReadySetupCycle)}";
    var firstOam = entry.OamDmcFirstPendingIndex is null && entry.OamDmcFirstReadyIndex is null
        ? "-"
        : $"{FormatNullableInt(entry.OamDmcFirstPendingIndex)}/{FormatNullableInt(entry.OamDmcFirstReadyIndex)}";
    return string.Create(
        CultureInfo.InvariantCulture,
        $"PC={pc:X4} CYC={cpuCycles} DMA={entry.Kind} addr={address} val={value} detail={detail} cpuAccess={entry.CpuAccessCycles} instrAccess={entry.InstructionAccessCycles} next={(entry.NextDmaCycleIsGet ? "get" : "put")} pending={entry.IsDmcPending}/{entry.IsDmcReady} retry={entry.DmcHaltRetry} loadDelay={entry.DmcLoadHaltDelayCycles} setup={setup} firstOam={firstOam} dmc={entry.PendingDmcKind}@${entry.PendingDmcAddress:X4}");
}

static bool ShouldTraceRead(ushort address, bool includeController)
{
    return (address is >= 0x2000 and <= 0x3FFF && (address & 0x0007) == 2) ||
        (includeController && address is (0x4016 or 0x4017));
}

static bool ShouldTraceWrite(ushort address, bool includeController)
{
    return address is >= 0x2000 and <= 0x3FFF or 0x4014 or >= 0x8000 ||
        (includeController && address == 0x4016);
}

static string FormatReadTrace(CpuBusReadDebugEntry entry, ushort pc)
{
    var register = entry.Address is 0x4016 or 0x4017
        ? "Controller"
        : "PPU$2002";
    return string.Create(
        CultureInfo.InvariantCulture,
        $"F{entry.PpuFrame,4} PC={pc:X4} SL{entry.PpuScanline,3} D{entry.PpuDot,3} ${entry.Address:X4}->${entry.Value:X2} {register} statusAfter=${entry.PpuStatusAfterRead:X2} ctrl=${entry.PpuControl:X2} mask=${entry.PpuMask:X2} v=${entry.CurrentVramAddress:X4} t=${entry.TemporaryVramAddress:X4} x={entry.FineX} scroll=({entry.ScrollX},{entry.ScrollY}) w={entry.WriteToggle}");
}

static string FormatWriteTrace(CpuBusWriteDebugEntry entry, IMapper mapper, ushort pc)
{
    var register = entry.Address is >= 0x2000 and <= 0x3FFF
        ? $"PPU${0x2000 + (entry.Address & 0x0007):X4}"
        : entry.Address == 0x4014
            ? "OAMDMA"
            : entry.Address == 0x4016
                ? "Controller strobe"
                : GetMapperWriteName(entry.Address);
    var line = string.Create(
        CultureInfo.InvariantCulture,
        $"F{entry.PpuFrame,4} PC={pc:X4} SL{entry.PpuScanline,3} D{entry.PpuDot,3} ${entry.Address:X4}<-${entry.Value:X2} {register} ctrl=${entry.PpuControl:X2} mask=${entry.PpuMask:X2} v=${entry.CurrentVramAddress:X4} t=${entry.TemporaryVramAddress:X4} x={entry.FineX} scroll=({entry.ScrollX},{entry.ScrollY}) w={entry.WriteToggle}");

    if (mapper is not Mapper4 mapper4 || entry.Address < 0x8000)
    {
        return line;
    }

    var state = mapper4.CaptureDebugState();
    return string.Create(
        CultureInfo.InvariantCulture,
        $"{line} bankSelect=${state.BankSelect:X2} regs={FormatBytes(state.BankRegisters)} chr={FormatInts(state.ChrBanks)} irq(l=${state.IrqLatch:X2},c=${state.IrqCounter:X2},r={state.IrqReload},e={state.IrqEnabled},p={state.IrqPending})");
}

static string GetMapperWriteName(ushort address)
{
    return (address & 0xE001) switch
    {
        0x8000 => "MMC3 bank select",
        0x8001 => "MMC3 bank data",
        0xA000 => "MMC3 mirroring",
        0xA001 => "MMC3 PRG RAM",
        0xC000 => "MMC3 IRQ latch",
        0xC001 => "MMC3 IRQ reload",
        0xE000 => "MMC3 IRQ disable",
        0xE001 => "MMC3 IRQ enable",
        _ => "Mapper write"
    };
}

static MachineFrameResult RunMachineToFrame(string romPath, string[] args)
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

    return new MachineFrameResult(
        machine,
        targetFrame,
        instructions,
        machine.PpuBus.Frame >= targetFrame);
}

static void PrintFrameDiagnosis(MachineFrameResult result)
{
    var machine = result.Machine;
    var ppu = machine.PpuBus.CaptureDebugState();
    Console.WriteLine($"Frame: {ppu.Frame} (target {result.TargetFrame})");
    Console.WriteLine($"Instructions: {result.Instructions}");
    Console.WriteLine($"PPU: scanline {ppu.Scanline}, dot {ppu.Dot}, total dots {ppu.TotalDots}");
    Console.WriteLine($"PPUCTRL: ${ppu.Control:X2}  PPUMASK: ${ppu.Mask:X2}  PPUSTATUS: ${ppu.Status:X2}");
    Console.WriteLine($"Effective mask: ${ppu.EffectiveMask:X2}  Pending mask: ${ppu.PendingMask:X2}  Pending mask delay: {ppu.PendingMaskDelayDots}");
    Console.WriteLine($"Rendering: enabled={ppu.IsRenderingEnabled}, bg={ppu.IsBackgroundEnabled}, sprites={ppu.IsSpriteRenderingEnabled}, left bg={ppu.ShowBackgroundInLeftColumn}, left sprites={ppu.ShowSpritesInLeftColumn}");
    Console.WriteLine($"Scroll: x={ppu.ScrollX}, y={ppu.ScrollY}, fineX={ppu.FineX}, writeToggle={ppu.WriteToggle}");
    Console.WriteLine($"VRAM: current=${ppu.CurrentVramAddress:X4}, temporary=${ppu.TemporaryVramAddress:X4}, readBuffer=${ppu.ReadBuffer:X2}");
    Console.WriteLine($"OAM: address=${ppu.OamAddress:X2}, scanline sprite y={ppu.ScanlineSpriteY}, scanline sprite count={ppu.ScanlineSpriteCount}");
    Console.WriteLine($"Palette RAM: {FormatBytes(ppu.PaletteRam)}");
    Console.WriteLine($"Nametable $2000 sample: {FormatBytes(ppu.NametableSample)}");
    Console.WriteLine($"OAM sprites 0-15: {FormatOamSprites(ppu.Oam, maxSprites: 16)}");
    Console.WriteLine($"Latched scanline sprites: {FormatLatchedSprites(ppu.ScanlineSprites)}");
    Console.WriteLine($"Background fetch: valid={ppu.BackgroundFetchValid}, render=${ppu.BackgroundFetchRenderAddress:X4}, tile=${ppu.BackgroundFetchTileIndex:X2}, attr={ppu.BackgroundFetchAttributePalette}, pattern=${ppu.BackgroundFetchPatternAddress:X4}, low=${ppu.BackgroundFetchPatternLow:X2}, high=${ppu.BackgroundFetchPatternHigh:X2}");
    Console.WriteLine($"Background shift: valid={ppu.BackgroundShiftValid}, render=${ppu.BackgroundShiftRenderAddress:X4}, next=${ppu.BackgroundShiftNextRenderAddress:X4}, patternLow=${ppu.BackgroundShiftPatternLow:X4}, patternHigh=${ppu.BackgroundShiftPatternHigh:X4}, attrLow=${ppu.BackgroundShiftAttributeLow:X4}, attrHigh=${ppu.BackgroundShiftAttributeHigh:X4}");
    Console.WriteLine($"Last frame background sources: shift={ppu.LastFrameBackgroundShiftPixels}, fallback={ppu.LastFrameBackgroundFallbackPixels}, current-address-transparent={ppu.LastFrameBackgroundCurrentAddressTransparentPixels}");
    Console.WriteLine($"Last frame HUD sources y192-239: shift={FormatScanlineBands(ppu.LastFrameBackgroundShiftPixelsByScanline, 192, 239)}, fallback={FormatScanlineBands(ppu.LastFrameBackgroundFallbackPixelsByScanline, 192, 239)}, current-address-transparent={FormatScanlineBands(ppu.LastFrameBackgroundCurrentAddressTransparentPixelsByScanline, 192, 239)}");

    var cartridge = machine.Cartridge;
    Console.WriteLine($"Cartridge: mapper {cartridge.Header.MapperNumber}, mirroring {cartridge.CurrentMirroringMode}, CHR {(cartridge.Header.UsesChrRam ? "RAM" : "ROM")} {cartridge.ChrMemory.Length} bytes");
    if (cartridge.Mapper is Mapper4 mapper4)
    {
        var mapper = mapper4.CaptureDebugState();
        Console.WriteLine("Mapper 4:");
        Console.WriteLine($"  bankSelect=${mapper.BankSelect:X2}, PRG inverted={mapper.IsPrgBankModeInverted}, CHR inverted={mapper.IsChrBankModeInverted}, mirroring={mapper.MirroringMode}");
        Console.WriteLine($"  bank registers: {FormatBytes(mapper.BankRegisters)}");
        Console.WriteLine($"  PRG 8K banks @8000/A000/C000/E000: {FormatInts(mapper.PrgBanks)}");
        Console.WriteLine($"  CHR 1K banks @0000..1C00: {FormatInts(mapper.ChrBanks)}");
        Console.WriteLine($"  IRQ latch=${mapper.IrqLatch:X2}, counter=${mapper.IrqCounter:X2}, reload={mapper.IrqReload}, enabled={mapper.IrqEnabled}, pending={mapper.IrqPending}, A12 high={mapper.PpuA12High}");
        Console.WriteLine($"  PRG RAM enabled={mapper.PrgRamEnabled}, writeProtected={mapper.PrgRamWriteProtected}");
    }
    else
    {
        Console.WriteLine($"Mapper diagnostics: mapper {cartridge.Header.MapperNumber} does not expose extended debug state.");
    }
}

static void WriteFrameDiagnosisDump(NesMachine machine, string directory)
{
    var ppu = machine.PpuBus.CaptureDebugState();
    Directory.CreateDirectory(directory);
    File.WriteAllBytes(Path.Combine(directory, "palette.bin"), ppu.PaletteRam);
    File.WriteAllBytes(Path.Combine(directory, "nametable.bin"), ppu.NametableRam);
    File.WriteAllBytes(Path.Combine(directory, "oam.bin"), ppu.Oam);
    File.WriteAllBytes(Path.Combine(directory, "chr.bin"), machine.Cartridge.ChrMemory);
    Console.WriteLine($"PPU state dump: {Path.GetFullPath(directory)}");
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

static bool HasOption(string[] args, string name)
{
    return args.Contains(name, StringComparer.Ordinal);
}

static bool IsBlarggComplete(NesMachine machine)
{
    return machine.CpuBus.ReadRaw(SprDmaReportData.BlarggSignatureAddress) == 0xDE &&
        machine.CpuBus.ReadRaw(SprDmaReportData.BlarggSignatureAddress + 1) == 0xB0 &&
        machine.CpuBus.ReadRaw(SprDmaReportData.BlarggSignatureAddress + 2) == 0x61 &&
        machine.CpuBus.ReadRaw(SprDmaReportData.BlarggStatusAddress) <= 0x7F;
}

static string ReadBlarggOutput(NesMachine machine)
{
    var chars = new List<char>();
    for (var i = 0; i < SprDmaReportData.BlarggMaxTextLength; i++)
    {
        var value = machine.CpuBus.ReadRaw((ushort)(SprDmaReportData.BlarggTextAddress + i));
        if (value == 0)
        {
            break;
        }

        chars.Add(value is >= 0x20 and <= 0x7E or 0x0A or 0x0D or 0x09 ? (char)value : '.');
    }

    return new string(chars.ToArray());
}

static int[] ParseSprDmaTimingRows(string output)
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

static bool IsSprDma512(string path)
{
    return Path.GetFileNameWithoutExtension(path).Contains("512", StringComparison.OrdinalIgnoreCase);
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

static string FormatBytes(IReadOnlyList<byte> values)
{
    return string.Join(" ", values.Select(value => $"${value:X2}"));
}

static string FormatInts(IReadOnlyList<int> values)
{
    return string.Join(" ", values.Select(value => value.ToString(CultureInfo.InvariantCulture)));
}

static string FormatScanlineBands(IReadOnlyList<int> values, int firstScanline, int lastScanline)
{
    const int bandHeight = 8;
    var bands = new List<string>();
    for (var y = firstScanline; y <= lastScanline; y += bandHeight)
    {
        var end = Math.Min(y + bandHeight - 1, lastScanline);
        var total = 0;
        for (var scanline = y; scanline <= end && scanline < values.Count; scanline++)
        {
            total += values[scanline];
        }

        bands.Add($"{y}-{end}:{total}");
    }

    return string.Join(" ", bands);
}

static string FormatOamSprites(IReadOnlyList<byte> oam, int maxSprites)
{
    var parts = new string[Math.Min(maxSprites, oam.Count / 4)];
    for (var i = 0; i < parts.Length; i++)
    {
        var offset = i * 4;
        parts[i] = $"#{i:D2}(y=${oam[offset]:X2},tile=${oam[offset + 1]:X2},attr=${oam[offset + 2]:X2},x=${oam[offset + 3]:X2})";
    }

    return string.Join(" ", parts);
}

static string FormatLatchedSprites(IReadOnlyList<NesSharp.Core.Ppu.SpriteDebugEntry> sprites)
{
    if (sprites.Count == 0)
    {
        return "<none>";
    }

    return string.Join(
        " ",
        sprites.Select(sprite =>
            $"#{sprite.SpriteIndex:D2}(x=${sprite.X:X2},attr=${sprite.Attributes:X2},lo=${sprite.PatternLow:X2},hi=${sprite.PatternHigh:X2})"));
}

static void PrintFrameSample(ReadOnlySpan<byte> framebuffer, ulong frame, long instructions)
{
    Span<int> histogram = stackalloc int[64];
    var nonzeroPixels = 0;
    foreach (var rawColor in framebuffer)
    {
        var color = rawColor & 0x3F;
        histogram[color]++;
        if (color != 0)
        {
            nonzeroPixels++;
        }
    }

    Span<int> topIndices = stackalloc int[8];
    topIndices.Fill(-1);
    for (var color = 0; color < histogram.Length; color++)
    {
        for (var slot = 0; slot < topIndices.Length; slot++)
        {
            var current = topIndices[slot];
            if (current >= 0 && histogram[current] >= histogram[color])
            {
                continue;
            }

            for (var move = topIndices.Length - 1; move > slot; move--)
            {
                topIndices[move] = topIndices[move - 1];
            }

            topIndices[slot] = color;
            break;
        }
    }

    var topColors = new string[topIndices.Length];
    for (var i = 0; i < topIndices.Length; i++)
    {
        var color = topIndices[i];
        topColors[i] = color < 0 ? string.Empty : $"${color:X2}:{histogram[color]}";
    }

    var hash = Convert.ToHexString(SHA256.HashData(framebuffer)).ToLowerInvariant();
    Console.WriteLine($"{frame},{instructions},{hash},{nonzeroPixels},{string.Join(" ", topColors)}");
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

internal readonly record struct MachineFrameResult(
    NesMachine Machine,
    ulong TargetFrame,
    long Instructions,
    bool Completed);

internal readonly record struct FrameScanResult(
    ulong Frame,
    long Instructions,
    FrameDiffResult Diff);

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

internal sealed class SprDmaTraceRow(int row)
{
    public int Row { get; } = row;

    public string StartNext { get; init; } = "-";

    public bool StartNextIsGet { get; init; }

    public int StartAccess { get; init; }

    public int StartInstructionAccess { get; init; }

    public bool StartPending { get; init; }

    public bool StartReady { get; init; }

    public bool StartDmcHaltRetry { get; init; }

    public int StartDmcLoadDelay { get; init; }

    public string? DmcKind { get; set; }

    public int? DmcOamIndex { get; set; }

    public int? DmcAccess { get; set; }

    public bool? DmcHaltRetry { get; set; }

    public int? DmcLoadDelay { get; set; }

    public int? FirstPendingSetupCycle { get; set; }

    public int? FirstReadySetupCycle { get; set; }

    public int? FirstPendingIndex { get; set; }

    public int? FirstReadyIndex { get; set; }

    public string? PreOamDmcKind { get; set; }

    public int? PreOamDmcDetail { get; set; }

    public int? PreOamDmcAccess { get; set; }

    public int? PreOamDmcInstructionAccess { get; set; }

    public ulong PreOamDmcCycleDelta { get; set; }

    public int? OamEndAccess { get; set; }

    public int StatusReads { get; set; }

    public int StatusWrites { get; set; }

    public int StatusDmcActiveReads { get; set; }

    public int StatusDmcInactiveReads { get; set; }
}

internal static class SprDmaReportData
{
    public const ushort BlarggStatusAddress = 0x6000;
    public const ushort BlarggSignatureAddress = 0x6001;
    public const ushort BlarggTextAddress = 0x6004;
    public const int BlarggMaxTextLength = 0x1FFC;

    public static readonly int[] ExpectedNormal =
    [
        527, 528, 527, 528,
        527, 526, 525, 526,
        525, 526, 525, 526,
        525, 526, 525, 526
    ];

    public static readonly int[] Expected512 =
    [
        525, 526, 525, 526,
        524, 525, 526, 527,
        527, 528, 526, 527,
        527, 528, 527, 528
    ];
}
