using NesSharp.Core.Cartridge;
using NesSharp.Core.Ppu;

namespace NesSharp.Tests;

public sealed class PpuBackgroundRenderingTests
{
    private const int ScreenWidth = 256;

    [Fact]
    public void BackgroundRenderingUsesCurrentVramAddress()
    {
        var ppu = CreatePpu();
        WritePatternRow(ppu, tile: 1, row: 0, low: 0xFF, high: 0x00);
        WritePalette(ppu, 0x3F01, 0x22);
        WriteNametableTile(ppu, x: 0, y: 0, tile: 0);
        WriteNametableTile(ppu, x: 1, y: 0, tile: 1);
        SetVramAddress(ppu, 0x0001);
        EnableBackground(ppu);

        ppu.Clock(4);

        Assert.Equal(0x22, ppu.Framebuffer[3]);
    }

    [Fact]
    public void BackgroundRenderingKeepsLoadedTileBitsForCurrentTile()
    {
        var ppu = CreatePpu();
        WritePatternRow(ppu, tile: 1, row: 0, low: 0xFF, high: 0x00);
        WritePalette(ppu, 0x3F01, 0x22);
        WriteNametableTile(ppu, x: 1, y: 0, tile: 1);
        EnableBackground(ppu);
        ppu.Clock(2);
        SetVramAddress(ppu, 0x0001);

        ppu.Clock(1);
        WritePatternRow(ppu, tile: 1, row: 0, low: 0x00, high: 0x00);
        ppu.Clock(1);

        Assert.Equal(0x22, ppu.Framebuffer[2]);
        Assert.Equal(0x22, ppu.Framebuffer[3]);
    }

    [Fact]
    public void ScheduledBackgroundFetchLoadsShiftRegister()
    {
        var ppu = CreatePpu();
        WritePatternRow(ppu, tile: 1, row: 0, low: 0xFF, high: 0x00);
        WritePalette(ppu, 0x3F01, 0x22);
        WriteNametableTile(ppu, x: 1, y: 0, tile: 1);
        SetVramAddress(ppu, 0x0001);
        EnableBackground(ppu);

        ppu.Clock(7);
        WritePatternRow(ppu, tile: 1, row: 0, low: 0x00, high: 0x00);
        ppu.Clock(1);

        Assert.Equal(0x22, ppu.Framebuffer[7]);
    }

    private static PpuBus CreatePpu() => new(CreateCartridge());

    private static void EnableBackground(PpuBus ppu)
    {
        ppu.WriteRegister(0x2001, 0x0A);
    }

    private static void SetVramAddress(PpuBus ppu, ushort address)
    {
        ppu.WriteRegister(0x2006, (byte)(address >> 8));
        ppu.WriteRegister(0x2006, (byte)address);
    }

    private static void WritePatternRow(PpuBus ppu, int tile, int row, byte low, byte high)
    {
        var address = (ushort)(tile * 16 + row);
        ppu.WritePatternTable(address, low);
        ppu.WritePatternTable((ushort)(address + 8), high);
    }

    private static void WritePalette(PpuBus ppu, ushort address, byte value)
    {
        SetVramAddress(ppu, address);
        ppu.WriteRegister(0x2007, value);
    }

    private static void WriteNametableTile(PpuBus ppu, int x, int y, byte tile)
    {
        SetVramAddress(ppu, (ushort)(0x2000 + y * 32 + x));
        ppu.WriteRegister(0x2007, tile);
    }

    private static Cartridge CreateCartridge()
    {
        var rom = new byte[16 + 16 * 1024 + 8 * 1024];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = 1;
        rom[5] = 0;
        return INesRomLoader.Load(rom);
    }
}
