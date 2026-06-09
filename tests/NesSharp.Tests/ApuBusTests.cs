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
    public void PulseControlRegisterCapturesDutyEnvelopeAndLengthHaltState()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4000, 0b1011_0110);

        Assert.Equal(2, apu.Pulse1.DutyCycle);
        Assert.True(apu.Pulse1.LengthCounterHalt);
        Assert.True(apu.Pulse1.ConstantVolume);
        Assert.Equal(6, apu.Pulse1.EnvelopePeriod);
        Assert.Equal(6, apu.Pulse1.EnvelopeOutput);
    }

    [Fact]
    public void PulseTimerRegistersFormElevenBitPeriod()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4002, 0xCD);
        apu.WriteRegister(0x4003, 0x83);

        Assert.Equal(0x03CD, apu.Pulse1.TimerPeriod);
    }

    [Fact]
    public void PulseLengthCounterHaltPreventsHalfFrameDecrement()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4015, 0x01);
        apu.WriteRegister(0x4000, 0x20);
        apu.WriteRegister(0x4003, 0x18);
        apu.WriteRegister(0x4017, 0x80);

        Assert.Equal(2, apu.Pulse1.LengthCounter);
    }

    [Fact]
    public void PulseEnvelopeRestartLoadsDecayOnQuarterFrame()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4015, 0x01);
        apu.WriteRegister(0x4000, 0x02);
        apu.WriteRegister(0x4003, 0x00);
        apu.WriteRegister(0x4017, 0x80);

        Assert.Equal(15, apu.Pulse1.EnvelopeOutput);
    }

    [Fact]
    public void PulseSweepRegisterCapturesSettingsAndTargetPeriod()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4002, 0x00);
        apu.WriteRegister(0x4003, 0x01);
        apu.WriteRegister(0x4001, 0b1001_0010);

        Assert.True(apu.Pulse1.SweepEnabled);
        Assert.Equal(1, apu.Pulse1.SweepPeriod);
        Assert.False(apu.Pulse1.SweepNegate);
        Assert.Equal(2, apu.Pulse1.SweepShift);
        Assert.Equal(0x0140, apu.Pulse1.SweepTargetPeriod);
    }

    [Fact]
    public void PulseOneNegativeSweepUsesOnesComplementAdjustment()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4002, 0x00);
        apu.WriteRegister(0x4003, 0x01);
        apu.WriteRegister(0x4001, 0b1001_1010);

        Assert.Equal(0x00BF, apu.Pulse1.SweepTargetPeriod);
    }

    [Fact]
    public void TriangleLinearCounterRegisterCapturesControlAndReloadValue()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4008, 0b1000_0111);

        Assert.True(apu.Triangle.ControlFlag);
        Assert.Equal(7, apu.Triangle.LinearCounterReloadValue);
    }

    [Fact]
    public void TriangleTimerRegistersFormElevenBitPeriod()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x400A, 0x34);
        apu.WriteRegister(0x400B, 0x85);

        Assert.Equal(0x0534, apu.Triangle.TimerPeriod);
    }

    [Fact]
    public void TriangleLengthCounterLoadsWhenChannelIsEnabled()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4015, 0x04);
        apu.WriteRegister(0x400B, 0x18);

        Assert.Equal(2, apu.Triangle.LengthCounter);
        Assert.Equal(0x04, apu.ReadStatus() & 0x04);
    }

    [Fact]
    public void TriangleLinearCounterReloadsOnQuarterFrameAndClearsWhenControlIsClear()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4015, 0x04);
        apu.WriteRegister(0x4008, 0x03);
        apu.WriteRegister(0x400B, 0x00);
        apu.WriteRegister(0x4017, 0x80);

        Assert.Equal(3, apu.Triangle.LinearCounter);

        apu.WriteRegister(0x4017, 0x80);

        Assert.Equal(2, apu.Triangle.LinearCounter);
    }

    [Fact]
    public void TriangleControlFlagKeepsLinearCounterReloadingAndHaltsLengthCounter()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4015, 0x04);
        apu.WriteRegister(0x4008, 0x82);
        apu.WriteRegister(0x400B, 0x18);
        apu.WriteRegister(0x4017, 0x80);

        Assert.Equal(2, apu.Triangle.LinearCounter);
        Assert.Equal(2, apu.Triangle.LengthCounter);

        apu.WriteRegister(0x4017, 0x80);

        Assert.Equal(2, apu.Triangle.LinearCounter);
        Assert.Equal(2, apu.Triangle.LengthCounter);
    }

    [Fact]
    public void TriangleSequencerRequiresLengthAndLinearCounters()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4015, 0x04);
        apu.WriteRegister(0x4008, 0x01);
        apu.WriteRegister(0x400B, 0x00);
        apu.WriteRegister(0x4017, 0x80);

        Assert.True(apu.Triangle.IsSequencerClocking);

        apu.WriteRegister(0x4015, 0x00);

        Assert.False(apu.Triangle.IsSequencerClocking);
    }

    [Fact]
    public void NoiseControlRegisterCapturesEnvelopeAndLengthHaltState()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x400C, 0b0011_0101);

        Assert.True(apu.Noise.LengthCounterHalt);
        Assert.True(apu.Noise.ConstantVolume);
        Assert.Equal(5, apu.Noise.EnvelopePeriod);
        Assert.Equal(5, apu.Noise.EnvelopeOutput);
    }

    [Fact]
    public void NoisePeriodRegisterCapturesModeAndPeriod()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x400E, 0x8F);

        Assert.True(apu.Noise.ModeFlag);
        Assert.Equal(0x0F, apu.Noise.PeriodIndex);
        Assert.Equal(4068, apu.Noise.TimerPeriod);
    }

    [Fact]
    public void NoiseLengthCounterLoadsWhenChannelIsEnabled()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4015, 0x08);
        apu.WriteRegister(0x400F, 0x18);

        Assert.Equal(2, apu.Noise.LengthCounter);
        Assert.Equal(0x08, apu.ReadStatus() & 0x08);
    }

    [Fact]
    public void NoiseEnvelopeRestartLoadsDecayOnQuarterFrame()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4015, 0x08);
        apu.WriteRegister(0x400C, 0x02);
        apu.WriteRegister(0x400F, 0x00);
        apu.WriteRegister(0x4017, 0x80);

        Assert.Equal(15, apu.Noise.EnvelopeOutput);
    }

    [Fact]
    public void NoiseLengthCounterHaltPreventsHalfFrameDecrement()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4015, 0x08);
        apu.WriteRegister(0x400C, 0x20);
        apu.WriteRegister(0x400F, 0x18);
        apu.WriteRegister(0x4017, 0x80);

        Assert.Equal(2, apu.Noise.LengthCounter);
    }

    [Fact]
    public void NoiseStatusClearsWhenDisabled()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4015, 0x08);
        apu.WriteRegister(0x400F, 0x00);
        apu.WriteRegister(0x4015, 0x00);

        Assert.Equal(0x00, apu.ReadStatus() & 0x08);
    }

    [Fact]
    public void NoiseShiftRegisterUsesSelectedFeedbackTap()
    {
        var normalMode = new ApuBus();
        var shortMode = new ApuBus();
        shortMode.WriteRegister(0x400E, 0x80);

        for (var i = 0; i < 10; i++)
        {
            normalMode.Noise.ClockShiftRegister();
            shortMode.Noise.ClockShiftRegister();
        }

        Assert.Equal(0x0020, normalMode.Noise.ShiftRegister);
        Assert.Equal(0x4020, shortMode.Noise.ShiftRegister);
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
        var machine = new NesMachine(CreateLoopCartridge());
        machine.Reset();

        for (var i = 0; i < 15_000; i++)
        {
            machine.StepInstruction();
        }

        Assert.Equal(0x40, machine.CpuBus.ApuBus.ReadStatus() & 0x40);
    }

    [Fact]
    public void ApuFrameInterruptRequestsCpuIrq()
    {
        var machine = new NesMachine(CreateLoopCartridge(irqVector: 0x9000));
        machine.Reset();

        for (var i = 0; i < 20_000 && machine.Cpu.ProgramCounter != 0x9000; i++)
        {
            machine.StepInstruction();
        }

        Assert.Equal(0x9000, machine.Cpu.ProgramCounter);
    }

    [Fact]
    public void ApuFrameIrqInhibitPreventsCpuIrq()
    {
        var machine = new NesMachine(CreateLoopCartridge(irqVector: 0x9000));
        machine.Reset();
        machine.CpuBus.Write(0x4017, 0x40);

        for (var i = 0; i < 20_000; i++)
        {
            machine.StepInstruction();
        }

        Assert.NotEqual(0x9000, machine.Cpu.ProgramCounter);
        Assert.Equal(0x00, machine.CpuBus.ApuBus.ReadStatus() & 0x40);
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

    private static Cartridge CreateLoopCartridge(ushort irqVector = 0x8000)
    {
        var rom = new byte[16 + 16 * 1024 + 8 * 1024];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = 1;
        rom[5] = 1;

        var prgOffset = 16;
        rom[prgOffset] = 0x58;
        rom[prgOffset + 1] = 0xEA;
        rom[prgOffset + 2] = 0x4C;
        rom[prgOffset + 3] = 0x01;
        rom[prgOffset + 4] = 0x80;
        rom[prgOffset + 0x1000] = 0xEA;
        rom[prgOffset + 0x3FFC] = 0x00;
        rom[prgOffset + 0x3FFD] = 0x80;
        rom[prgOffset + 0x3FFE] = (byte)(irqVector & 0x00FF);
        rom[prgOffset + 0x3FFF] = (byte)(irqVector >> 8);

        return INesRomLoader.Load(rom);
    }
}
