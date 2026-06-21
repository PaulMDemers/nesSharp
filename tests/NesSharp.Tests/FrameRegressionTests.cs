using System.Security.Cryptography;
using NesSharp.Core.Ppu;
using NesSharp.Core.Runtime;

namespace NesSharp.Tests;

public sealed class FrameRegressionTests
{
    [Fact]
    public void PpuReadBufferFrame120MatchesExpectedRgbHash()
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        var romPath = Path.Combine(root, "test-roms", "nes-test-roms", "ppu_read_buffer", "test_ppu_read_buffer.nes");
        var machine = NesMachine.LoadFile(romPath);
        machine.Reset();

        while (machine.PpuBus.Frame < 120)
        {
            machine.StepInstruction();
        }

        var hash = Convert.ToHexString(SHA256.HashData(ToRgb(machine.PpuBus.Framebuffer))).ToLowerInvariant();

        Assert.Equal("5d092be6e1f42fb9ed98fc617474388319925445e6453b3fb84c2591d248037a", hash);
    }

    private static byte[] ToRgb(ReadOnlySpan<byte> framebuffer)
    {
        var rgb = new byte[framebuffer.Length * 3];
        for (var i = 0; i < framebuffer.Length; i++)
        {
            var color = NesPalette.GetRgb(framebuffer[i]);
            var offset = i * 3;
            rgb[offset] = color.R;
            rgb[offset + 1] = color.G;
            rgb[offset + 2] = color.B;
        }

        return rgb;
    }

    private static string FindWorkspaceRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms", "ppu_read_buffer", "test_ppu_read_buffer.nes")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing ppu_read_buffer.");
    }
}
