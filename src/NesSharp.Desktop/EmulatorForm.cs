using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using NesSharp.Core.Cartridge;
using NesSharp.Core.Input;
using NesSharp.Core.Ppu;
using NesSharp.Core.Runtime;

namespace NesSharp.Desktop;

internal sealed class EmulatorForm : Form
{
    private const int TargetFrameMilliseconds = 16;
    private const long MaxInstructionsPerFrame = 500_000;

    private readonly NesDisplayControl display = new();
    private readonly ToolStripStatusLabel statusLabel = new();
    private readonly ToolStripMenuItem resetMenuItem = new("&Reset");
    private readonly ToolStripMenuItem powerCycleMenuItem = new("&Power Cycle");
    private readonly ToolStripMenuItem pauseMenuItem = new("&Pause") { CheckOnClick = true };
    private readonly ToolStripMenuItem frameAdvanceMenuItem = new("&Frame Advance");
    private readonly ToolStripMenuItem captureDiagnosticsMenuItem = new("&Capture Diagnostics");
    private readonly Lock machineLock = new();
    private readonly WaveOutAudioPlayer audioPlayer = new();

    private NesMachine? machine;
    private string? romPath;
    private string? savePath;
    private ControllerButton controllerState;
    private CancellationTokenSource? emulationCancellation;
    private Task? emulationTask;
    private string? audioStatus;
    private int pendingFramePresentations;

    public EmulatorForm(IReadOnlyList<string> args)
    {
        Text = "nesSharp";
        ClientSize = new Size(NesDisplayControl.NesWidth * 3, NesDisplayControl.NesHeight * 3);
        MinimumSize = new Size(NesDisplayControl.NesWidth + 16, NesDisplayControl.NesHeight + 88);
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;

        var menuStrip = BuildMenu();
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(statusLabel);

        display.Dock = DockStyle.Fill;

        Controls.Add(display);
        Controls.Add(statusStrip);
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;

        UpdateUiState();

        if (args.Count > 0)
        {
            LoadRom(args[0]);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (machine is null)
        {
            BeginInvoke(ShowOpenRomDialog);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (SetControllerButton(e.KeyCode, pressed: true))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (SetControllerButton(e.KeyCode, pressed: false))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        base.OnKeyUp(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var keyCode = keyData & Keys.KeyCode;
        if (keyCode == Keys.Space && (keyData & Keys.Modifiers) == Keys.None && pauseMenuItem.Enabled)
        {
            pauseMenuItem.Checked = !pauseMenuItem.Checked;
            return true;
        }

        if (MapKey(keyCode) is not null)
        {
            SetControllerButton(keyCode, pressed: true);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopEmulationLoop();
            SaveCurrentBatteryRam();
            display.Dispose();
            audioPlayer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopEmulationLoop();
        SaveCurrentBatteryRam();
        base.OnFormClosing(e);
    }

    private MenuStrip BuildMenu()
    {
        var openMenuItem = new ToolStripMenuItem("&Open ROM...", null, (_, _) => ShowOpenRomDialog(), Keys.Control | Keys.O);
        resetMenuItem.ShortcutKeys = Keys.Control | Keys.R;
        resetMenuItem.Click += (_, _) => ResetMachine();
        powerCycleMenuItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.R;
        powerCycleMenuItem.Click += (_, _) => PowerCycleMachine();
        frameAdvanceMenuItem.ShortcutKeys = Keys.F10;
        frameAdvanceMenuItem.Click += (_, _) => StepFrame();
        captureDiagnosticsMenuItem.ShortcutKeys = Keys.Control | Keys.D;
        captureDiagnosticsMenuItem.Click += (_, _) => CaptureDiagnostics();
        pauseMenuItem.ShortcutKeyDisplayString = "Space";
        pauseMenuItem.CheckedChanged += (_, _) =>
        {
            if (pauseMenuItem.Checked)
            {
                StopEmulationLoop();
            }
            else
            {
                StartEmulationLoop();
            }

            UpdateUiState();
        };

        var fileMenu = new ToolStripMenuItem("&File");
        fileMenu.DropDownItems.Add(openMenuItem);
        fileMenu.DropDownItems.Add(resetMenuItem);
        fileMenu.DropDownItems.Add(powerCycleMenuItem);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close(), Keys.Alt | Keys.F4));

        var emulationMenu = new ToolStripMenuItem("&Emulation");
        emulationMenu.DropDownItems.Add(pauseMenuItem);
        emulationMenu.DropDownItems.Add(frameAdvanceMenuItem);
        emulationMenu.DropDownItems.Add(captureDiagnosticsMenuItem);

        var menuStrip = new MenuStrip();
        menuStrip.Items.Add(fileMenu);
        menuStrip.Items.Add(emulationMenu);
        return menuStrip;
    }

    private void ShowOpenRomDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "NES ROMs (*.nes)|*.nes|All files (*.*)|*.*",
            Title = "Open NES ROM",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            LoadRom(dialog.FileName);
        }
    }

    private void LoadRom(string path)
    {
        try
        {
            StopEmulationLoop();
            SaveCurrentBatteryRam();

            var loadedSavePath = Path.ChangeExtension(path, ".sav");
            var loadedMachine = LoadMachine(path, loadedSavePath);

            machine = loadedMachine;
            romPath = path;
            savePath = loadedSavePath;
            controllerState = 0;
            machine.Controller1.State = controllerState;
            pauseMenuItem.Checked = false;
            display.UpdateFrame(machine.PpuBus.Framebuffer);
            UpdateUiState();
            StartEmulationLoop();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidRomException or NotSupportedException)
        {
            MessageBox.Show(this, ex.Message, "Unable to open ROM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateUiState();
        }
    }

    private void PowerCycleMachine()
    {
        if (romPath is null)
        {
            return;
        }

        try
        {
            var wasPaused = pauseMenuItem.Checked;
            StopEmulationLoop();
            SaveCurrentBatteryRam();

            var loadedMachine = LoadMachine(romPath, savePath ?? Path.ChangeExtension(romPath, ".sav"));
            lock (machineLock)
            {
                machine = loadedMachine;
                controllerState = 0;
                machine.Controller1.State = controllerState;
                display.UpdateFrame(machine.PpuBus.Framebuffer);
            }

            pauseMenuItem.Checked = wasPaused;
            UpdateUiState();
            if (!pauseMenuItem.Checked)
            {
                StartEmulationLoop();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidRomException or NotSupportedException)
        {
            pauseMenuItem.Checked = true;
            MessageBox.Show(this, ex.Message, "Unable to power cycle", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateUiState();
        }
    }

    private void ResetMachine()
    {
        if (machine is null)
        {
            return;
        }

        StopEmulationLoop();
        SaveCurrentBatteryRam();
        lock (machineLock)
        {
            machine.Reset();
            controllerState = 0;
            machine.Controller1.State = controllerState;
            display.UpdateFrame(machine.PpuBus.Framebuffer);
        }

        UpdateUiState();
        if (!pauseMenuItem.Checked)
        {
            StartEmulationLoop();
        }
    }

    private void StartEmulationLoop()
    {
        if (machine is null || pauseMenuItem.Checked || emulationTask is { IsCompleted: false })
        {
            return;
        }

        try
        {
            audioPlayer.Start();
            audioStatus = null;
        }
        catch (Exception ex) when (ex is Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            audioStatus = "audio unavailable";
        }

        emulationCancellation = new CancellationTokenSource();
        var token = emulationCancellation.Token;
        emulationTask = Task.Run(() => RunEmulationLoop(token), token);
    }

    private void StopEmulationLoop()
    {
        var cancellation = emulationCancellation;
        if (cancellation is null)
        {
            audioPlayer.Stop();
            return;
        }

        emulationCancellation = null;
        cancellation.Cancel();
        try
        {
            emulationTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is TaskCanceledException or OperationCanceledException))
        {
        }
        finally
        {
            audioPlayer.Stop();
            cancellation.Dispose();
            emulationTask = null;
        }
    }

    private void RunEmulationLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var frameStartedAt = Stopwatch.GetTimestamp();
            var framebuffer = new byte[NesDisplayControl.NesWidth * NesDisplayControl.NesHeight];
            var audioSamples = Array.Empty<float>();
            ulong frame = 0;
            var frameLimited = false;

            try
            {
                lock (machineLock)
                {
                    if (machine is null)
                    {
                        return;
                    }

                    var result = RunMachineUntilNextFrame(machine, token);
                    framebuffer = result.Framebuffer;
                    audioSamples = machine.CpuBus.ApuBus.DrainSamples();
                    frame = result.Frame;
                    frameLimited = result.FrameLimited;
                }
            }
            catch (Exception ex)
            {
                PostEmulationError(ex);
                return;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            PostFrame(framebuffer, frame, frameLimited);
            audioPlayer.Enqueue(audioSamples);
            WaitForNextFrame(frameStartedAt, token);
        }
    }

    private void StepFrame()
    {
        if (machine is null)
        {
            return;
        }

        if (!pauseMenuItem.Checked)
        {
            pauseMenuItem.Checked = true;
        }
        else
        {
            StopEmulationLoop();
        }

        byte[] framebuffer;
        ulong frame;
        bool frameLimited;

        lock (machineLock)
        {
            if (machine is null)
            {
                return;
            }

            var result = RunMachineUntilNextFrame(machine);
            framebuffer = result.Framebuffer;
            frame = result.Frame;
            frameLimited = result.FrameLimited;
            machine.CpuBus.ApuBus.DrainSamples();
        }

        display.UpdateFrame(framebuffer);
        UpdateUiState(frameLimited, frame);
    }

    private static FrameStepResult RunMachineUntilNextFrame(NesMachine targetMachine, CancellationToken token = default)
    {
        var startFrame = targetMachine.PpuBus.Frame;
        long instructions = 0;
        while (targetMachine.PpuBus.Frame == startFrame &&
            instructions < MaxInstructionsPerFrame &&
            !token.IsCancellationRequested)
        {
            targetMachine.StepInstruction();
            instructions++;
        }

        return new FrameStepResult(
            targetMachine.PpuBus.Framebuffer.ToArray(),
            targetMachine.PpuBus.Frame,
            instructions >= MaxInstructionsPerFrame);
    }

    private void PostFrame(byte[] framebuffer, ulong frame, bool frameLimited)
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        if (Interlocked.Exchange(ref pendingFramePresentations, 1) == 1)
        {
            return;
        }

        try
        {
            BeginInvoke((MethodInvoker)(() =>
            {
                try
                {
                    if (IsDisposed)
                    {
                        return;
                    }

                    display.UpdateFrame(framebuffer);
                    UpdateUiState(frameLimited, frame);
                }
                finally
                {
                    Volatile.Write(ref pendingFramePresentations, 0);
                }
            }));
        }
        catch (InvalidOperationException)
        {
            Volatile.Write(ref pendingFramePresentations, 0);
        }
    }

    private void PostEmulationError(Exception ex)
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        try
        {
            BeginInvoke((MethodInvoker)(() =>
            {
                pauseMenuItem.Checked = true;
                MessageBox.Show(this, ex.Message, "Emulation stopped", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void WaitForNextFrame(long frameStartedAt, CancellationToken token)
    {
        var elapsedMilliseconds = (Stopwatch.GetTimestamp() - frameStartedAt) * 1000.0 / Stopwatch.Frequency;
        var remainingMilliseconds = TargetFrameMilliseconds - (int)Math.Ceiling(elapsedMilliseconds);
        if (remainingMilliseconds > 0)
        {
            token.WaitHandle.WaitOne(remainingMilliseconds);
        }
    }

    private void UpdateUiState(bool frameLimited = false, ulong? renderedFrame = null)
    {
        var hasRom = machine is not null;
        resetMenuItem.Enabled = hasRom;
        powerCycleMenuItem.Enabled = hasRom;
        pauseMenuItem.Enabled = hasRom;
        frameAdvanceMenuItem.Enabled = hasRom;
        captureDiagnosticsMenuItem.Enabled = hasRom;

        if (!hasRom)
        {
            statusLabel.Text = "No ROM loaded";
            return;
        }

        var state = pauseMenuItem.Checked ? "paused" : "running";
        var limited = frameLimited ? " - frame budget reached" : string.Empty;
        var audio = audioStatus is null ? string.Empty : $" - {audioStatus}";
        statusLabel.Text = $"{Path.GetFileName(romPath)} - frame {renderedFrame ?? machine!.PpuBus.Frame} - {state}{limited}{audio}";
    }

    private NesMachine LoadMachine(string path, string loadedSavePath)
    {
        var loadedMachine = NesMachine.LoadFile(path);
        loadedMachine.Reset();
        LoadBatteryRam(loadedMachine, loadedSavePath);
        return loadedMachine;
    }

    private void LoadBatteryRam(NesMachine loadedMachine, string loadedSavePath)
    {
        if (!loadedMachine.Cartridge.HasBatteryBackedSaveRam || !File.Exists(loadedSavePath))
        {
            return;
        }

        var data = File.ReadAllBytes(loadedSavePath);
        loadedMachine.Cartridge.LoadSaveRam(data);
    }

    private void SaveCurrentBatteryRam()
    {
        string? targetPath;
        byte[] saveBytes;
        lock (machineLock)
        {
            if (machine is null || savePath is null || !machine.Cartridge.HasBatteryBackedSaveRam)
            {
                return;
            }

            targetPath = savePath;
            saveBytes = machine.Cartridge.SaveRam.ToArray();
        }

        if (targetPath is null)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(targetPath, saveBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            statusLabel.Text = $"Could not write save file: {ex.Message}";
        }
    }

    private void CaptureDiagnostics()
    {
        byte[] framebuffer;
        PpuDebugState ppu;
        Mapper4DebugState? mapper4 = null;
        string? loadedRomPath;

        lock (machineLock)
        {
            if (machine is null)
            {
                return;
            }

            framebuffer = machine.PpuBus.Framebuffer.ToArray();
            ppu = machine.PpuBus.CaptureDebugState();
            if (machine.Cartridge.Mapper is Mapper4 mapper)
            {
                mapper4 = mapper.CaptureDebugState();
            }

            loadedRomPath = romPath;
        }

        try
        {
            var captureDirectory = GetCaptureDirectory();
            Directory.CreateDirectory(captureDirectory);

            var romName = Path.GetFileNameWithoutExtension(loadedRomPath) ?? "rom";
            var safeRomName = string.Join("_", romName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var prefix = $"{safeRomName}_frame{ppu.Frame:D6}";
            var imagePath = Path.Combine(captureDirectory, $"{prefix}.bmp");
            var textPath = Path.Combine(captureDirectory, $"{prefix}.txt");

            WriteFrameBitmap(imagePath, framebuffer);
            File.WriteAllText(textPath, BuildDiagnosticText(loadedRomPath, ppu, mapper4), Encoding.UTF8);
            statusLabel.Text = $"Captured diagnostics: {Path.GetFileName(imagePath)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException)
        {
            statusLabel.Text = $"Could not capture diagnostics: {ex.Message}";
        }
    }

    private static string GetCaptureDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NesSharp.slnx")))
            {
                return Path.Combine(directory.FullName, "artifacts", "desktop-captures");
            }

            directory = directory.Parent;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "nesSharp-captures");
    }

    private static void WriteFrameBitmap(string path, ReadOnlySpan<byte> framebuffer)
    {
        using var bitmap = new Bitmap(NesDisplayControl.NesWidth, NesDisplayControl.NesHeight, PixelFormat.Format24bppRgb);
        var bounds = new Rectangle(0, 0, NesDisplayControl.NesWidth, NesDisplayControl.NesHeight);
        var data = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var stride = data.Stride;
            var bytes = new byte[stride * NesDisplayControl.NesHeight];
            for (var y = 0; y < NesDisplayControl.NesHeight; y++)
            {
                for (var x = 0; x < NesDisplayControl.NesWidth; x++)
                {
                    var color = NesPalette.GetRgb(framebuffer[y * NesDisplayControl.NesWidth + x]);
                    var offset = y * stride + x * 3;
                    bytes[offset] = color.B;
                    bytes[offset + 1] = color.G;
                    bytes[offset + 2] = color.R;
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        bitmap.Save(path, ImageFormat.Bmp);
    }

    private static string BuildDiagnosticText(string? loadedRomPath, PpuDebugState ppu, Mapper4DebugState? mapper4)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"ROM: {loadedRomPath ?? "<none>"}");
        builder.AppendLine($"Frame: {ppu.Frame}");
        builder.AppendLine($"PPU: scanline {ppu.Scanline}, dot {ppu.Dot}, total dots {ppu.TotalDots}");
        builder.AppendLine($"PPUCTRL: ${ppu.Control:X2}  PPUMASK: ${ppu.Mask:X2}  PPUSTATUS: ${ppu.Status:X2}");
        builder.AppendLine($"Effective mask: ${ppu.EffectiveMask:X2}  Pending mask: ${ppu.PendingMask:X2}  Pending mask delay: {ppu.PendingMaskDelayDots}");
        builder.AppendLine($"Rendering: enabled={ppu.IsRenderingEnabled}, bg={ppu.IsBackgroundEnabled}, sprites={ppu.IsSpriteRenderingEnabled}, left bg={ppu.ShowBackgroundInLeftColumn}, left sprites={ppu.ShowSpritesInLeftColumn}");
        builder.AppendLine($"Scroll: x={ppu.ScrollX}, y={ppu.ScrollY}, fineX={ppu.FineX}, writeToggle={ppu.WriteToggle}");
        builder.AppendLine($"VRAM: current=${ppu.CurrentVramAddress:X4}, temporary=${ppu.TemporaryVramAddress:X4}, readBuffer=${ppu.ReadBuffer:X2}");
        builder.AppendLine($"OAM: address=${ppu.OamAddress:X2}, scanline sprite y={ppu.ScanlineSpriteY}, scanline sprite count={ppu.ScanlineSpriteCount}");
        builder.AppendLine($"Palette RAM: {FormatBytes(ppu.PaletteRam)}");
        builder.AppendLine($"Nametable $2000 sample: {FormatBytes(ppu.NametableSample)}");
        builder.AppendLine($"OAM sprites 0-63: {FormatOamSprites(ppu.Oam, maxSprites: 64)}");
        builder.AppendLine($"Latched scanline sprites: {FormatLatchedSprites(ppu.ScanlineSprites)}");
        builder.AppendLine($"Background fetch: valid={ppu.BackgroundFetchValid}, render=${ppu.BackgroundFetchRenderAddress:X4}, tile=${ppu.BackgroundFetchTileIndex:X2}, attr={ppu.BackgroundFetchAttributePalette}, pattern=${ppu.BackgroundFetchPatternAddress:X4}, low=${ppu.BackgroundFetchPatternLow:X2}, high=${ppu.BackgroundFetchPatternHigh:X2}");
        builder.AppendLine($"Background shift: valid={ppu.BackgroundShiftValid}, render=${ppu.BackgroundShiftRenderAddress:X4}, next=${ppu.BackgroundShiftNextRenderAddress:X4}, patternLow=${ppu.BackgroundShiftPatternLow:X4}, patternHigh=${ppu.BackgroundShiftPatternHigh:X4}, attrLow=${ppu.BackgroundShiftAttributeLow:X4}, attrHigh=${ppu.BackgroundShiftAttributeHigh:X4}");
        builder.AppendLine($"Last frame background sources: shift={ppu.LastFrameBackgroundShiftPixels}, fallback={ppu.LastFrameBackgroundFallbackPixels}, current-address-transparent={ppu.LastFrameBackgroundCurrentAddressTransparentPixels}");
        builder.AppendLine($"Last frame HUD sources y192-239: shift={FormatScanlineBands(ppu.LastFrameBackgroundShiftPixelsByScanline, 192, 239)}, fallback={FormatScanlineBands(ppu.LastFrameBackgroundFallbackPixelsByScanline, 192, 239)}, current-address-transparent={FormatScanlineBands(ppu.LastFrameBackgroundCurrentAddressTransparentPixelsByScanline, 192, 239)}");

        if (mapper4 is not null)
        {
            var mapper = mapper4.Value;
            builder.AppendLine("Mapper 4:");
            builder.AppendLine($"  bankSelect=${mapper.BankSelect:X2}, PRG inverted={mapper.IsPrgBankModeInverted}, CHR inverted={mapper.IsChrBankModeInverted}, mirroring={mapper.MirroringMode}");
            builder.AppendLine($"  bank registers: {FormatBytes(mapper.BankRegisters)}");
            builder.AppendLine($"  PRG 8K banks @8000/A000/C000/E000: {FormatInts(mapper.PrgBanks)}");
            builder.AppendLine($"  CHR 1K banks @0000..1C00: {FormatInts(mapper.ChrBanks)}");
            builder.AppendLine($"  IRQ latch=${mapper.IrqLatch:X2}, counter=${mapper.IrqCounter:X2}, reload={mapper.IrqReload}, enabled={mapper.IrqEnabled}, pending={mapper.IrqPending}, A12 high={mapper.PpuA12High}");
            builder.AppendLine($"  PRG RAM enabled={mapper.PrgRamEnabled}, writeProtected={mapper.PrgRamWriteProtected}");
        }

        return builder.ToString();
    }

    private static string FormatBytes(IReadOnlyList<byte> values)
    {
        return string.Join(" ", values.Select(value => $"${value:X2}"));
    }

    private static string FormatInts(IReadOnlyList<int> values)
    {
        return string.Join(" ", values.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static string FormatScanlineBands(IReadOnlyList<int> values, int firstScanline, int lastScanline)
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

    private static string FormatOamSprites(IReadOnlyList<byte> oam, int maxSprites)
    {
        var parts = new string[Math.Min(maxSprites, oam.Count / 4)];
        for (var i = 0; i < parts.Length; i++)
        {
            var offset = i * 4;
            parts[i] = $"#{i:D2}(y=${oam[offset]:X2},tile=${oam[offset + 1]:X2},attr=${oam[offset + 2]:X2},x=${oam[offset + 3]:X2})";
        }

        return string.Join(" ", parts);
    }

    private static string FormatLatchedSprites(IReadOnlyList<SpriteDebugEntry> sprites)
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

    private bool SetControllerButton(Keys key, bool pressed)
    {
        var button = MapKey(key);
        if (button is null)
        {
            return false;
        }

        controllerState = pressed
            ? controllerState | button.Value
            : controllerState & ~button.Value;

        lock (machineLock)
        {
            if (machine is not null)
            {
                machine.Controller1.State = controllerState;
            }
        }

        return true;
    }

    private static ControllerButton? MapKey(Keys key)
    {
        return key switch
        {
            Keys.Z => ControllerButton.A,
            Keys.X => ControllerButton.B,
            Keys.Back => ControllerButton.Select,
            Keys.Enter => ControllerButton.Start,
            Keys.Up => ControllerButton.Up,
            Keys.Down => ControllerButton.Down,
            Keys.Left => ControllerButton.Left,
            Keys.Right => ControllerButton.Right,
            _ => null
        };
    }
}

internal readonly record struct FrameStepResult(byte[] Framebuffer, ulong Frame, bool FrameLimited);
