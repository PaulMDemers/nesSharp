using NesSharp.Core.Apu;
using NesSharp.Core.Cartridge;
using NesSharp.Core.Memory;
using NesSharp.Core.Runtime;

namespace NesSharp.Tests;

public sealed class ApuBusTests
{
    [Fact]
    public void StatusReadReportsEnabledLengthCounters()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4015, 0x01);
        apu.WriteRegister(0x4003, 0x00);

        Assert.Equal(0x01, apu.ReadStatus() & 0x01);
    }

    [Fact]
    public void DisablingChannelClearsItsLengthCounterStatus()
    {
        var apu = new ApuBus();
        apu.WriteRegister(0x4015, 0x01);
        apu.WriteRegister(0x4003, 0x00);

        apu.WriteRegister(0x4015, 0x00);

        Assert.Equal(0x00, apu.ReadStatus() & 0x01);
    }

    [Fact]
    public void FourStepFrameCounterRaisesFrameInterruptStatus()
    {
        var apu = new ApuBus();

        for (var i = 0; i < 29_829; i++)
        {
            apu.Clock();
        }

        Assert.Equal(0x40, apu.ReadStatus() & 0x40);
        Assert.Equal(0x00, apu.ReadStatus() & 0x40);
    }

    [Fact]
    public void FrameCounterIrqInhibitClearsFrameInterruptStatus()
    {
        var apu = new ApuBus();
        for (var i = 0; i < 29_829; i++)
        {
            apu.Clock();
        }

        apu.WriteRegister(0x4017, 0x40);

        Assert.Equal(0x00, apu.ReadStatus() & 0x40);
        Assert.Equal(0x40, apu.FrameCounterControl);
    }

    [Fact]
    public void FiveStepFrameCounterWriteImmediatelyClocksLengthCounters()
    {
        var apu = new ApuBus();
        apu.WriteRegister(0x4015, 0x01);
        apu.WriteRegister(0x4003, 0x18);

        apu.WriteRegister(0x4017, 0x80);

        Assert.Equal(0x01, apu.ReadStatus() & 0x01);

        apu.WriteRegister(0x4017, 0x80);

        Assert.Equal(0x00, apu.ReadStatus() & 0x01);
    }

    [Fact]
    public void CpuBusRoutesApuStatusAndFrameCounterWrites()
    {
        var bus = new CpuBus(CreateCartridge());

        bus.Write(0x4015, 0x01);
        bus.Write(0x4003, 0x00);
        bus.Write(0x4017, 0x40);

        Assert.Equal(0x01, bus.Read(0x4015) & 0x01);
        Assert.Equal(0x40, bus.ApuBus.FrameCounterControl);
    }

    [Fact]
    public void NesMachineClocksApuDuringCpuExecution()
    {
        var machine = new NesMachine(CreateInfiniteNopCartridge());
        machine.Reset();

        for (var i = 0; i < 15_000; i++)
        {
            machine.StepInstruction();
        }

        Assert.Equal(0x40, machine.CpuBus.ApuBus.ReadStatus() & 0x40);
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

    private static Cartridge CreateInfiniteNopCartridge()
    {
        var rom = new byte[16 + 16 * 1024 + 8 * 1024];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = 1;
        rom[5] = 1;

        var prgOffset = 16;
        rom[prgOffset] = 0xEA;
        rom[prgOffset + 1] = 0x4C;
        rom[prgOffset + 2] = 0x00;
        rom[prgOffset + 3] = 0x80;
        rom[prgOffset + 0x3FFC] = 0x00;
        rom[prgOffset + 0x3FFD] = 0x80;

        return INesRomLoader.Load(rom);
    }
}
