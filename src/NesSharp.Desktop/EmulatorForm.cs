using NesSharp.Core.Cartridge;
using NesSharp.Core.Input;
using NesSharp.Core.Runtime;

namespace NesSharp.Desktop;

internal sealed class EmulatorForm : Form
{
    private const int TargetFrameMilliseconds = 16;
    private const long MaxInstructionsPerTick = 500_000;

    private readonly NesDisplayControl display = new();
    private readonly ToolStripStatusLabel statusLabel = new();
    private readonly ToolStripMenuItem resetMenuItem = new("&Reset");
    private readonly ToolStripMenuItem pauseMenuItem = new("&Pause") { CheckOnClick = true };
    private readonly System.Windows.Forms.Timer emulationTimer = new() { Interval = TargetFrameMilliseconds };

    private NesMachine? machine;
    private string? romPath;
    private string? savePath;
    private ControllerButton controllerState;

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

        emulationTimer.Tick += (_, _) => RunOneFrame();
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
            SaveCurrentBatteryRam();
            emulationTimer.Dispose();
            display.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveCurrentBatteryRam();
        base.OnFormClosing(e);
    }

    private MenuStrip BuildMenu()
    {
        var openMenuItem = new ToolStripMenuItem("&Open ROM...", null, (_, _) => ShowOpenRomDialog(), Keys.Control | Keys.O);
        resetMenuItem.ShortcutKeys = Keys.Control | Keys.R;
        resetMenuItem.Click += (_, _) => ResetMachine();
        pauseMenuItem.ShortcutKeys = Keys.Space;
        pauseMenuItem.CheckedChanged += (_, _) => UpdateUiState();

        var fileMenu = new ToolStripMenuItem("&File");
        fileMenu.DropDownItems.Add(openMenuItem);
        fileMenu.DropDownItems.Add(resetMenuItem);
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
            emulationTimer.Stop();
            SaveCurrentBatteryRam();

            var loadedMachine = NesMachine.LoadFile(path);
            loadedMachine.Reset();
            var loadedSavePath = Path.ChangeExtension(path, ".sav");
            LoadBatteryRam(loadedMachine, loadedSavePath);

            machine = loadedMachine;
            romPath = path;
            savePath = loadedSavePath;
            controllerState = 0;
            machine.Controller1.State = controllerState;
            pauseMenuItem.Checked = false;
            display.UpdateFrame(machine.PpuBus.Framebuffer);
            UpdateUiState();
            emulationTimer.Start();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidRomException or NotSupportedException)
        {
            MessageBox.Show(this, ex.Message, "Unable to open ROM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateUiState();
        }
    }

    private void ResetMachine()
    {
        if (machine is null)
        {
            return;
        }

        SaveCurrentBatteryRam();
        machine.Reset();
        controllerState = 0;
        machine.Controller1.State = controllerState;
        display.UpdateFrame(machine.PpuBus.Framebuffer);
        UpdateUiState();
    }

    private void RunOneFrame()
    {
        if (machine is null || pauseMenuItem.Checked)
        {
            return;
        }

        var startFrame = machine.PpuBus.Frame;
        long instructions = 0;
        try
        {
            while (machine.PpuBus.Frame == startFrame && instructions < MaxInstructionsPerTick)
            {
                machine.StepInstruction();
                instructions++;
            }
        }
        catch (Exception ex)
        {
            emulationTimer.Stop();
            pauseMenuItem.Checked = true;
            MessageBox.Show(this, ex.Message, "Emulation stopped", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        display.UpdateFrame(machine.PpuBus.Framebuffer);
        UpdateUiState(instructions >= MaxInstructionsPerTick);
    }

    private void UpdateUiState(bool frameLimited = false)
    {
        var hasRom = machine is not null;
        resetMenuItem.Enabled = hasRom;
        pauseMenuItem.Enabled = hasRom;

        if (!hasRom)
        {
            statusLabel.Text = "No ROM loaded";
            return;
        }

        var state = pauseMenuItem.Checked ? "paused" : "running";
        var limited = frameLimited ? " - frame budget reached" : string.Empty;
        statusLabel.Text = $"{Path.GetFileName(romPath)} - frame {machine!.PpuBus.Frame} - {state}{limited}";
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
        if (machine is null || savePath is null || !machine.Cartridge.HasBatteryBackedSaveRam)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(savePath, machine.Cartridge.SaveRam.ToArray());
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

        if (machine is not null)
        {
            machine.Controller1.State = controllerState;
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
