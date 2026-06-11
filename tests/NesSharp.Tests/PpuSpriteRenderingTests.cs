using NesSharp.Core.Cartridge;
using NesSharp.Core.Ppu;

namespace NesSharp.Tests;

public sealed class PpuSpriteRenderingTests
{
    private const int ScreenWidth = 256;

    [Fact]
    public void SpritePixelRendersWithSpritePalette()
    {
        var ppu = CreatePpu();
        WritePatternRow(ppu, tile: 1, row: 0, low: 0b1000_0000, high: 0);
        WritePalette(ppu, 0x3F11, 0x24);
        WriteSprite(ppu, index: 0, y: 8, tile: 1, attributes: 0x00, x: 8);
        EnableSprites(ppu);

        ClockThroughPixel(ppu, x: 8, y: 8);

        Assert.Equal(0x24, ppu.Framebuffer[8 * ScreenWidth + 8]);
    }

    [Fact]
    public void EarlierOamSpriteWinsOverLaterOverlappingSprite()
    {
        var ppu = CreatePpu();
        WritePatternRow(ppu, tile: 1, row: 0, low: 0b1000_0000, high: 0);
        WritePatternRow(ppu, tile: 2, row: 0, low: 0, high: 0b1000_0000);
        WritePalette(ppu, 0x3F11, 0x21);
        WritePalette(ppu, 0x3F12, 0x32);
        WriteSprite(ppu, index: 0, y: 8, tile: 1, attributes: 0x00, x: 8);
        WriteSprite(ppu, index: 1, y: 8, tile: 2, attributes: 0x00, x: 8);
        EnableSprites(ppu);

        ClockThroughPixel(ppu, x: 8, y: 8);

        Assert.Equal(0x21, ppu.Framebuffer[8 * ScreenWidth + 8]);
    }

    [Fact]
    public void SpritePriorityBehindBackgroundUsesBackgroundPixel()
    {
        var ppu = CreatePpu();
        WritePatternRow(ppu, tile: 0, row: 0, low: 0b1000_0000, high: 0);
        WritePatternRow(ppu, tile: 1, row: 0, low: 0, high: 0b1000_0000);
        WritePalette(ppu, 0x3F01, 0x12);
        WritePalette(ppu, 0x3F12, 0x26);
        WriteNametableTile(ppu, x: 1, y: 1, tile: 0);
        WriteSprite(ppu, index: 0, y: 8, tile: 1, attributes: 0x20, x: 8);
        SetVramAddress(ppu, 0x0000);
        EnableBackgroundAndSprites(ppu);

        ClockThroughPixel(ppu, x: 8, y: 8);

        Assert.Equal(0x12, ppu.Framebuffer[8 * ScreenWidth + 8]);
    }

    [Fact]
    public void SpriteHorizontalFlipMirrorsPixelsInsideBoundingBox()
    {
        var ppu = CreatePpu();
        WritePatternRow(ppu, tile: 1, row: 0, low: 0b1000_0000, high: 0);
        WritePalette(ppu, 0x3F11, 0x24);
        WriteSprite(ppu, index: 0, y: 8, tile: 1, attributes: 0x40, x: 8);
        EnableSprites(ppu);

        ClockThroughPixel(ppu, x: 15, y: 8);

        Assert.Equal(0x24, ppu.Framebuffer[8 * ScreenWidth + 15]);
    }

    [Fact]
    public void SpriteVerticalFlipMirrorsPixelsInsideBoundingBox()
    {
        var ppu = CreatePpu();
        WritePatternRow(ppu, tile: 1, row: 0, low: 0b1000_0000, high: 0);
        WritePalette(ppu, 0x3F11, 0x24);
        WriteSprite(ppu, index: 0, y: 8, tile: 1, attributes: 0x80, x: 8);
        EnableSprites(ppu);

        ClockThroughPixel(ppu, x: 8, y: 15);

        Assert.Equal(0x24, ppu.Framebuffer[15 * ScreenWidth + 8]);
    }

    [Fact]
    public void SpriteAttributeSelectsSpritePalette()
    {
        var ppu = CreatePpu();
        WritePatternRow(ppu, tile: 1, row: 0, low: 0b1000_0000, high: 0);
        WritePalette(ppu, 0x3F11, 0x11);
        WritePalette(ppu, 0x3F19, 0x29);
        WriteSprite(ppu, index: 0, y: 8, tile: 1, attributes: 0x02, x: 8);
        EnableSprites(ppu);

        ClockThroughPixel(ppu, x: 8, y: 8);

        Assert.Equal(0x29, ppu.Framebuffer[8 * ScreenWidth + 8]);
    }

    [Fact]
    public void EightBySixteenSpriteUsesTileBitZeroAsPatternBank()
    {
        var ppu = CreatePpu();
        WritePatternRow(ppu, tile: 0x02, row: 0, low: 0b1000_0000, high: 0);
        WritePatternRow(ppu, tile: 0x102, row: 0, low: 0, high: 0b1000_0000);
        WritePalette(ppu, 0x3F11, 0x21);
        WritePalette(ppu, 0x3F12, 0x32);
        ppu.WriteRegister(0x2000, 0x20);
        WriteSprite(ppu, index: 0, y: 8, tile: 0x03, attributes: 0x00, x: 8);
        EnableSprites(ppu);

        ClockThroughPixel(ppu, x: 8, y: 8);

        Assert.Equal(0x32, ppu.Framebuffer[8 * ScreenWidth + 8]);
    }

    [Fact]
    public void NinthSpriteOnScanlineDoesNotRender()
    {
        var ppu = CreatePpu();
        WritePatternRow(ppu, tile: 1, row: 0, low: 0b1000_0000, high: 0);
        WritePalette(ppu, 0x3F11, 0x24);
        for (var i = 0; i < 8; i++)
        {
            WriteSprite(ppu, index: i, y: 8, tile: 0, attributes: 0x00, x: 32);
        }

        WriteSprite(ppu, index: 8, y: 8, tile: 1, attributes: 0x00, x: 8);
        EnableSprites(ppu);

        ClockThroughPixel(ppu, x: 8, y: 8);

        Assert.Equal(0x00, ppu.Framebuffer[8 * ScreenWidth + 8]);
    }

    [Fact]
    public void NinthSpriteOnScanlineSetsSpriteOverflowStatus()
    {
        var ppu = CreatePpu();
        for (var i = 0; i < 9; i++)
        {
            WriteSprite(ppu, index: i, y: 8, tile: 0, attributes: 0x00, x: 8);
        }

        EnableSprites(ppu);

        ClockThroughPixel(ppu, x: 8, y: 8);

        Assert.Equal(0x20, ppu.ReadRegister(0x2002) & 0x20);
    }

    private static PpuBus CreatePpu() => new(CreateCartridge());

    private static void EnableSprites(PpuBus ppu)
    {
        ppu.WriteRegister(0x2001, 0x14);
    }

    private static void EnableBackgroundAndSprites(PpuBus ppu)
    {
        ppu.WriteRegister(0x2001, 0x1E);
    }

    private static void ClockThroughPixel(PpuBus ppu, int x, int y)
    {
        ppu.Clock(y * 341 + x + 1);
    }

    private static void WriteSprite(PpuBus ppu, int index, byte y, byte tile, byte attributes, byte x)
    {
        ppu.WriteRegister(0x2003, (byte)(index * 4));
        ppu.WriteRegister(0x2004, (byte)(y - 1));
        ppu.WriteRegister(0x2004, tile);
        ppu.WriteRegister(0x2004, attributes);
        ppu.WriteRegister(0x2004, x);
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
        var address = (ushort)(0x2000 + y * 32 + x);
        SetVramAddress(ppu, address);
        ppu.WriteRegister(0x2007, tile);
    }

    private static void SetVramAddress(PpuBus ppu, ushort address)
    {
        ppu.WriteRegister(0x2006, (byte)(address >> 8));
        ppu.WriteRegister(0x2006, (byte)address);
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
