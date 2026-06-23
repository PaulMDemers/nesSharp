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
        EnableBackground(ppu);
        ppu.Clock(8);
        SetVramAddress(ppu, 0x0001);

        ppu.Clock(7);
        WritePatternRow(ppu, tile: 1, row: 0, low: 0x00, high: 0x00);
        ppu.Clock(1);

        Assert.Equal(0x22, ppu.Framebuffer[15]);
    }

    [Fact]
    public void ScheduledBackgroundShifterAdvancesAcrossPixels()
    {
        var ppu = CreatePpu();
        WritePatternRow(ppu, tile: 1, row: 0, low: 0b0100_0000, high: 0x00);
        WritePalette(ppu, 0x3F01, 0x22);
        WriteNametableTile(ppu, x: 1, y: 0, tile: 1);
        EnableBackground(ppu);
        ppu.Clock(8);
        SetVramAddress(ppu, 0x0001);

        ppu.Clock(8);

        Assert.Equal(0x00, ppu.Framebuffer[14]);
        Assert.Equal(0x22, ppu.Framebuffer[15]);
    }

    [Fact]
    public void ScheduledBackgroundShifterUsesHighPatternPlane()
    {
        var ppu = CreatePpu();
        WritePatternRow(ppu, tile: 1, row: 0, low: 0x00, high: 0b0100_0000);
        WritePalette(ppu, 0x3F02, 0x27);
        WriteNametableTile(ppu, x: 1, y: 0, tile: 1);
        EnableBackground(ppu);
        ppu.Clock(8);
        SetVramAddress(ppu, 0x0001);

        ppu.Clock(8);

        Assert.Equal(0x00, ppu.Framebuffer[14]);
        Assert.Equal(0x27, ppu.Framebuffer[15]);
    }

    [Fact]
    public void ScheduledBackgroundShifterLoadsNextTileIntoLowByte()
    {
        var ppu = CreatePpu();
        WritePatternRow(ppu, tile: 1, row: 0, low: 0x00, high: 0x00);
        WritePatternRow(ppu, tile: 2, row: 0, low: 0x80, high: 0x00);
        WritePalette(ppu, 0x3F01, 0x22);
        WriteNametableTile(ppu, x: 1, y: 0, tile: 1);
        WriteNametableTile(ppu, x: 2, y: 0, tile: 2);
        EnableBackground(ppu);
        ppu.Clock(8);
        SetVramAddress(ppu, 0x0001);

        ppu.Clock(23);

        Assert.Equal(0x00, ppu.Framebuffer[22]);
        Assert.Equal(0x22, ppu.Framebuffer[30]);
    }

    [Fact]
    public void ScheduledBackgroundAttributeShifterLoadsNextPaletteIntoLowByte()
    {
        var ppu = CreatePpu();
        WritePatternRow(ppu, tile: 1, row: 0, low: 0x00, high: 0x00);
        WritePatternRow(ppu, tile: 2, row: 0, low: 0x80, high: 0x00);
        WritePalette(ppu, 0x3F05, 0x25);
        WriteNametableTile(ppu, x: 1, y: 0, tile: 1);
        WriteNametableTile(ppu, x: 2, y: 0, tile: 2);
        WriteAttributeByte(ppu, 0x23C0, 0b0000_0100);
        EnableBackground(ppu);
        ppu.Clock(8);
        SetVramAddress(ppu, 0x0001);

        ppu.Clock(23);

        Assert.Equal(0x25, ppu.Framebuffer[30]);
    }

    [Fact]
    public void PaletteWritesStoreSixBitColorValue()
    {
        var ppu = CreatePpu();

        WritePalette(ppu, 0x3F03, 0x40);

        Assert.Equal(0x00, ppu.CaptureDebugState().PaletteRam[0x03]);
    }

    [Fact]
    public void ScrollBackgroundUsesScheduledShifterAfterFetch()
    {
        var ppu = CreatePpu();
        WritePatternRow(ppu, tile: 1, row: 0, low: 0xFF, high: 0x00);
        WritePalette(ppu, 0x3F01, 0x22);
        WriteNametableTile(ppu, x: 0, y: 0, tile: 1);
        WriteNametableTile(ppu, x: 1, y: 0, tile: 1);
        SetVramAddress(ppu, 0x0000);
        ppu.WriteRegister(0x2005, 0x00);
        ppu.WriteRegister(0x2005, 0x00);
        EnableBackground(ppu);
        ppu.Clock(8);

        ppu.Clock(7);
        WritePatternRow(ppu, tile: 1, row: 0, low: 0x00, high: 0x00);
        ppu.Clock(1);

        Assert.Equal(0x22, ppu.Framebuffer[15]);
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

    private static void WriteAttributeByte(PpuBus ppu, ushort address, byte value)
    {
        SetVramAddress(ppu, address);
        ppu.WriteRegister(0x2007, value);
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
