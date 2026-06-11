using NesSharp.Core.Cartridge;
using NesSharp.Core.Ppu;

namespace NesSharp.Tests;

public sealed class PpuScrollAddressTests
{
    [Fact]
    public void RenderingIncrementsCoarseXEveryEightVisibleDots()
    {
        var ppu = CreatePpu();
        WriteNametableByte(ppu, 0x2001, 0x5A);
        SetVramAddress(ppu, 0x2000);
        EnableBackground(ppu);

        ppu.Clock(8);

        Assert.Equal(0x5A, ReadBufferedCurrentVramByte(ppu));
    }

    [Fact]
    public void PreRenderLineCopiesScrollAddressIntoCurrentVramAddress()
    {
        var ppu = CreatePpu();
        ppu.WritePatternTable(0x0020, 0x6B);
        SetVramAddress(ppu, 0x2000);
        ppu.WriteRegister(0x2000, 0x00);
        ppu.WriteRegister(0x2005, 0x00);
        ppu.WriteRegister(0x2005, 0x08);
        EnableBackground(ppu);

        ClockTo(ppu, frame: 0, scanline: 261, dot: 304);

        Assert.Equal(0x6B, ReadBufferedCurrentVramByte(ppu));
    }

    private static PpuBus CreatePpu()
    {
        return new PpuBus(CreateCartridge());
    }

    private static void EnableBackground(PpuBus ppu)
    {
        ppu.WriteRegister(0x2001, 0x08);
    }

    private static void SetVramAddress(PpuBus ppu, ushort address)
    {
        ppu.WriteRegister(0x2006, (byte)(address >> 8));
        ppu.WriteRegister(0x2006, (byte)address);
    }

    private static void WriteNametableByte(PpuBus ppu, ushort address, byte value)
    {
        SetVramAddress(ppu, address);
        ppu.WriteRegister(0x2007, value);
    }

    private static byte ReadBufferedCurrentVramByte(PpuBus ppu)
    {
        ppu.ReadRegister(0x2007);
        return ppu.ReadRegister(0x2007);
    }

    private static void ClockTo(PpuBus ppu, ulong frame, int scanline, int dot)
    {
        const int maxDots = 200_000;
        var dots = 0;
        while (IsBefore(ppu, frame, scanline, dot))
        {
            ppu.Clock(1);
            dots++;

            if (dots > maxDots)
            {
                throw new InvalidOperationException($"Could not reach PPU state {frame}:{scanline}:{dot}.");
            }
        }

        Assert.Equal(frame, ppu.Frame);
        Assert.Equal(scanline, ppu.Scanline);
        Assert.Equal(dot, ppu.Dot);
    }

    private static bool IsBefore(PpuBus ppu, ulong frame, int scanline, int dot)
    {
        return ppu.Frame < frame ||
            (ppu.Frame == frame &&
                (ppu.Scanline < scanline ||
                    (ppu.Scanline == scanline && ppu.Dot < dot)));
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
