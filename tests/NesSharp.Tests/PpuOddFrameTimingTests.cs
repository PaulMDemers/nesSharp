using NesSharp.Core.Cartridge;
using NesSharp.Core.Memory;
using NesSharp.Core.Ppu;

namespace NesSharp.Tests;

public sealed class PpuOddFrameTimingTests
{
    [Fact]
    public void PpuMaskWriteAtPreRenderDot337QualifiesForOddFrameSkip()
    {
        var ppu = CreatePpu();
        ClockTo(ppu, frame: 1, scanline: 261, dot: 337);

        ppu.WriteRegister(0x2001, 0x08);
        ppu.Clock(2);

        Assert.Equal(1UL, ppu.Frame);
        Assert.Equal(261, ppu.Scanline);
        Assert.Equal(339, ppu.Dot);

        ppu.Clock(1);

        Assert.Equal(2UL, ppu.Frame);
        Assert.Equal(0, ppu.Scanline);
        Assert.Equal(0, ppu.Dot);
    }

    [Fact]
    public void PpuMaskWriteAtPreRenderDot338MissesOddFrameSkip()
    {
        var ppu = CreatePpu();
        ClockTo(ppu, frame: 1, scanline: 261, dot: 338);

        ppu.WriteRegister(0x2001, 0x08);
        ppu.Clock(1);

        Assert.Equal(1UL, ppu.Frame);
        Assert.Equal(261, ppu.Scanline);
        Assert.Equal(339, ppu.Dot);

        ppu.Clock(1);

        Assert.Equal(1UL, ppu.Frame);
        Assert.Equal(261, ppu.Scanline);
        Assert.Equal(340, ppu.Dot);
    }

    [Fact]
    public void CpuPpuMaskWriteStartingAtPreRenderDot334QualifiesForOddFrameSkip()
    {
        var cartridge = CreateCartridge();
        var ppu = new PpuBus(cartridge);
        var bus = new CpuBus(cartridge, ppu);
        bus.SetCpuCycleCallback(() => ppu.Clock(3));
        ClockTo(ppu, frame: 1, scanline: 261, dot: 334);

        bus.BeginCpuInstruction();
        bus.Write(0x2001, 0x08);
        bus.EndCpuInstruction();

        Assert.Equal(1UL, ppu.Frame);
        Assert.Equal(261, ppu.Scanline);
        Assert.Equal(337, ppu.Dot);

        ppu.Clock(2);

        Assert.Equal(1UL, ppu.Frame);
        Assert.Equal(261, ppu.Scanline);
        Assert.Equal(339, ppu.Dot);

        ppu.Clock(1);

        Assert.Equal(2UL, ppu.Frame);
        Assert.Equal(0, ppu.Scanline);
        Assert.Equal(0, ppu.Dot);
    }

    [Fact]
    public void CpuPpuMaskWriteStartingAtPreRenderDot335MissesOddFrameSkip()
    {
        var cartridge = CreateCartridge();
        var ppu = new PpuBus(cartridge);
        var bus = new CpuBus(cartridge, ppu);
        bus.SetCpuCycleCallback(() => ppu.Clock(3));
        ClockTo(ppu, frame: 1, scanline: 261, dot: 335);

        bus.BeginCpuInstruction();
        bus.Write(0x2001, 0x08);
        bus.EndCpuInstruction();

        Assert.Equal(1UL, ppu.Frame);
        Assert.Equal(261, ppu.Scanline);
        Assert.Equal(338, ppu.Dot);

        ppu.Clock(1);

        Assert.Equal(1UL, ppu.Frame);
        Assert.Equal(261, ppu.Scanline);
        Assert.Equal(339, ppu.Dot);

        ppu.Clock(1);

        Assert.Equal(1UL, ppu.Frame);
        Assert.Equal(261, ppu.Scanline);
        Assert.Equal(340, ppu.Dot);
    }

    private static PpuBus CreatePpu()
    {
        return new PpuBus(CreateCartridge());
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
        rom[5] = 1;

        return INesRomLoader.Load(rom);
    }
}
