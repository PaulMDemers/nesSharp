using NesSharp.Core.Runtime;
using NesSharp.Core.Testing;
using NesSharp.Core.Input;

namespace NesSharp.Tests;

public sealed class ReadJoyRomTests
{
    public static TheoryData<string> PassingReadJoyRoms => new()
    {
        "count_errors.nes",
        "count_errors_fast.nes"
    };

    [Theory]
    [MemberData(nameof(PassingReadJoyRoms))]
    public void ReadJoyRomsPass(string romName)
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "read_joy3", romName);

        var result = ShellExitTestRunner.Run(NesMachine.LoadFile(romPath), 100_000_000);

        Assert.True(
            result.Passed,
            $"{romName} failed with status {result.Status}, code {result.ResultCode:X2}, after {result.InstructionsExecuted} instructions.");
    }

    [Fact]
    public void TestButtonsRomPassesWithScriptedInput()
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "read_joy3", "test_buttons.nes");

        var result = ShellExitTestRunner.Run(
            NesMachine.LoadFile(romPath),
            5_000_000,
            (machine, instructions) => machine.Controller1.State = GetScriptedButtonState(instructions));

        Assert.True(
            result.Passed,
            $"test_buttons.nes failed with status {result.Status}, code {result.ResultCode:X2}, after {result.InstructionsExecuted} instructions.");
    }

    private static string FindWorkspaceRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "read_joy3")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing read_joy3 test ROMs.");
    }

    private static ControllerButton GetScriptedButtonState(long instructions)
    {
        ControllerButton[] buttons =
        [
            ControllerButton.A,
            ControllerButton.B,
            ControllerButton.Select,
            ControllerButton.Start,
            ControllerButton.Up,
            ControllerButton.Down,
            ControllerButton.Left,
            ControllerButton.Right
        ];

        const long initialDelay = 200_000;
        const long pressDuration = 50_000;
        const long releaseDuration = 150_000;
        const long buttonPeriod = pressDuration + releaseDuration;

        var scriptTime = instructions - initialDelay;
        if (scriptTime < 0)
        {
            return 0;
        }

        var buttonIndex = scriptTime / buttonPeriod;
        if (buttonIndex >= buttons.Length)
        {
            return 0;
        }

        return scriptTime % buttonPeriod < pressDuration
            ? buttons[buttonIndex]
            : 0;
    }
}
