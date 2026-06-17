using System.ComponentModel;
using System.Diagnostics;
using NesSharp.Core.Cartridge;
using NesSharp.Core.Input;
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

                    var startFrame = machine.PpuBus.Frame;
                    long instructions = 0;
                    while (machine.PpuBus.Frame == startFrame &&
                        instructions < MaxInstructionsPerFrame &&
                        !token.IsCancellationRequested)
                    {
                        machine.StepInstruction();
                        instructions++;
                    }

                    machine.PpuBus.Framebuffer.CopyTo(framebuffer);
                    audioSamples = machine.CpuBus.ApuBus.DrainSamples();
                    frame = machine.PpuBus.Frame;
                    frameLimited = instructions >= MaxInstructionsPerFrame;
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
