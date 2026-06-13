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
    public void PulseChannelOutputsEnvelopeWhenSequencerAndLengthAreActive()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4015, 0x01);
        apu.WriteRegister(0x4000, 0b1101_1111);
        apu.WriteRegister(0x4002, 0x08);
        apu.WriteRegister(0x4003, 0x00);

        Assert.Equal(15, apu.Pulse1.OutputLevel);
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
    public void DmcControlRegisterCapturesIrqLoopAndRate()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4010, 0b1100_1111);

        Assert.True(apu.Dmc.IrqEnabled);
        Assert.True(apu.Dmc.Loop);
        Assert.Equal(0x0F, apu.Dmc.RateIndex);
        Assert.Equal(54, apu.Dmc.TimerPeriod);
    }

    [Fact]
    public void DmcDirectLoadStoresSevenBitOutputLevel()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4011, 0xFF);

        Assert.Equal(0x7F, apu.Dmc.OutputLevel);
    }

    [Fact]
    public void DmcSampleAddressAndLengthRegistersUseHardwareFormula()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4012, 0x34);
        apu.WriteRegister(0x4013, 0x12);

        Assert.Equal(0xCD00, apu.Dmc.SampleAddress);
        Assert.Equal(0x0121, apu.Dmc.SampleLength);
    }

    [Fact]
    public void DmcEnableStartsSampleAndStatusReportsBytesRemaining()
    {
        var apu = new ApuBus();
        apu.WriteRegister(0x4012, 0x01);
        apu.WriteRegister(0x4013, 0x02);

        apu.WriteRegister(0x4015, 0x10);

        Assert.Equal(0xC040, apu.Dmc.CurrentAddress);
        Assert.Equal(33, apu.Dmc.BytesRemaining);
        Assert.True(apu.Dmc.SampleBufferEmpty);
        Assert.True(apu.IsDmcDmaPending);
        Assert.False(apu.IsDmcDmaReady);
        Assert.Equal(0xC040, apu.PendingDmcDmaAddress);
        Assert.Equal(0x10, apu.ReadStatus() & 0x10);
    }

    [Fact]
    public void DmcDisableClearsBytesRemainingAndStatusBit()
    {
        var apu = new ApuBus();
        apu.WriteRegister(0x4015, 0x10);

        apu.WriteRegister(0x4015, 0x00);

        Assert.Equal(0, apu.Dmc.BytesRemaining);
        Assert.Equal(0x00, apu.ReadStatus() & 0x10);
    }

    [Fact]
    public void DmcControlWriteClearsInterruptWhenIrqDisabled()
    {
        var apu = new ApuBus();
        apu.WriteRegister(0x4010, 0x80);
        apu.WriteRegister(0x4015, 0x10);

        apu.Dmc.MarkSampleByteRead();
        apu.WriteRegister(0x4010, 0x00);

        Assert.False(apu.Dmc.InterruptFlag);
        Assert.Equal(0x00, apu.ReadStatus() & 0x80);
    }

    [Fact]
    public void DmcSampleCompletionSetsIrqWhenEnabledAndNotLooping()
    {
        var apu = new ApuBus();
        apu.WriteRegister(0x4010, 0x80);
        apu.WriteRegister(0x4015, 0x10);

        apu.Dmc.MarkSampleByteRead();

        Assert.True(apu.Dmc.InterruptFlag);
        Assert.Equal(0x80, apu.ReadStatus() & 0x80);
        Assert.True(apu.IsDmcInterruptPending);
    }

    [Fact]
    public void DmcSampleCompletionLoopsWhenLoopFlagIsSet()
    {
        var apu = new ApuBus();
        apu.WriteRegister(0x4010, 0x40);
        apu.WriteRegister(0x4013, 0x01);
        apu.WriteRegister(0x4015, 0x10);

        for (var i = 0; i < 17; i++)
        {
            apu.Dmc.MarkSampleByteRead();
        }

        Assert.Equal(17, apu.Dmc.BytesRemaining);
        Assert.False(apu.Dmc.InterruptFlag);
    }

    [Fact]
    public void DmcAddressWrapsToEightThousandAfterFfff()
    {
        var apu = new ApuBus();
        apu.WriteRegister(0x4012, 0xFF);
        apu.WriteRegister(0x4013, 0x05);
        apu.WriteRegister(0x4015, 0x10);

        for (var i = 0; i < 64; i++)
        {
            apu.Dmc.MarkSampleByteRead();
        }

        Assert.Equal(0x8000, apu.Dmc.CurrentAddress);
    }

    [Fact]
    public void DmcInterruptRequestsCpuIrqThroughMachine()
    {
        var machine = new NesMachine(CreateLoopCartridge(irqVector: 0x9000));
        machine.Reset();
        machine.StepInstruction();
        machine.CpuBus.Write(0x4010, 0x80);
        machine.CpuBus.Write(0x4015, 0x10);

        for (var i = 0; i < 10 && machine.Cpu.ProgramCounter != 0x9000; i++)
        {
            machine.StepInstruction();
        }

        Assert.Equal(0x9000, machine.Cpu.ProgramCounter);
    }

    [Fact]
    public void DmcPlaybackUsesSampleReaderAndDeltaBits()
    {
        var apu = new ApuBus();
        apu.WriteRegister(0x4010, 0x0F);
        apu.WriteRegister(0x4011, 64);
        apu.WriteRegister(0x4013, 0x00);
        apu.WriteRegister(0x4015, 0x10);

        Assert.True(apu.IsDmcDmaPending);
        Assert.Equal(0xC000, apu.PendingDmcDmaAddress);
        apu.CompleteDmcDma(0b0000_0011);
        ClockDmcTimerTicks(apu, 10);

        Assert.True(apu.Dmc.SampleBufferEmpty);
        Assert.False(apu.Dmc.Silence);
        Assert.True(apu.Dmc.OutputLevel > 64);
    }

    [Fact]
    public void DmcSampleReaderUsesCpuBusMappedMemory()
    {
        var bus = new CpuBus(CreateCartridge(dmcSample: 0b0000_0011));
        bus.Write(0x4010, 0x0F);
        bus.Write(0x4011, 64);
        bus.Write(0x4013, 0x00);
        bus.Write(0x4015, 0x10);
        ClockDmcDmaReady(bus.ApuBus);
        bus.Read(0x8000);

        ClockDmcTimerTicks(bus.ApuBus, 10);

        Assert.True(bus.ApuBus.Dmc.OutputLevel > 64);
    }

    [Fact]
    public void DmcLoadDmaAddsThreeCpuCycles()
    {
        var bus = new CpuBus(CreateCartridge());
        bus.WriteRaw(0x4013, 0x00);

        bus.BeginCpuInstruction();
        bus.Write(0x4015, 0x10);
        bus.EndCpuInstruction();

        Assert.Equal(1, bus.CpuAccessCycles);
        Assert.Equal(1, bus.InstructionAccessCycles);
        Assert.True(bus.ApuBus.IsDmcDmaPending);
        Assert.False(bus.ApuBus.IsDmcDmaReady);

        ClockDmcDmaReady(bus.ApuBus);
        Assert.True(bus.ApuBus.IsDmcDmaReady);

        bus.BeginCpuInstruction();
        bus.Read(0x8000);
        bus.EndCpuInstruction();

        Assert.Equal(4, bus.CpuAccessCycles);
        Assert.Equal(1, bus.InstructionAccessCycles);
        Assert.False(bus.ApuBus.IsDmcDmaPending);
        Assert.Equal(0, bus.ApuBus.Dmc.BytesRemaining);
    }

    [Fact]
    public void DmcDmaAddsAlignmentCycleWhenGetPhaseIsMissed()
    {
        var bus = new CpuBus(CreateCartridge());
        bus.WriteRaw(0x4013, 0x00);

        bus.BeginCpuInstruction();
        bus.Write(0x4015, 0x10);
        bus.EndCpuInstruction();
        ClockDmcDmaReady(bus.ApuBus);

        bus.BeginCpuInstruction();
        bus.Write(0x0000, 0x00);
        bus.EndCpuInstruction();

        bus.BeginCpuInstruction();
        bus.Read(0x8000);
        bus.EndCpuInstruction();

        Assert.Equal(5, bus.CpuAccessCycles);
        Assert.Equal(1, bus.InstructionAccessCycles);
        Assert.False(bus.ApuBus.IsDmcDmaPending);
    }

    [Fact]
    public void DmcReloadDmaAddsFourCpuCycles()
    {
        var bus = new CpuBus(CreateCartridge(dmcSample: 0b0000_0011));
        bus.Write(0x4010, 0x0F);
        bus.Write(0x4011, 64);
        bus.Write(0x4013, 0x01);
        bus.Write(0x4015, 0x10);
        ClockDmcDmaReady(bus.ApuBus);
        bus.Read(0x8000);
        ClockDmcTimerTicks(bus.ApuBus, 8);
        ClockDmcDmaReady(bus.ApuBus);

        Assert.True(bus.ApuBus.IsDmcDmaPending);

        bus.BeginCpuInstruction();
        bus.Read(0x8000);
        bus.EndCpuInstruction();

        Assert.Equal(5, bus.CpuAccessCycles);
        Assert.Equal(1, bus.InstructionAccessCycles);
        Assert.False(bus.ApuBus.IsDmcDmaPending);
    }

    [Fact]
    public void MixerIncludesDmcDirectOutput()
    {
        var apu = new ApuBus();

        apu.WriteRegister(0x4011, 0x7F);

        Assert.True(apu.MixSample() > 0);
    }

    [Fact]
    public void ClockingApuProducesDrainableSamples()
    {
        var apu = new ApuBus();
        apu.WriteRegister(0x4011, 0x7F);

        for (var i = 0; i < 100; i++)
        {
            apu.Clock();
        }

        Assert.True(apu.PendingSampleCount > 0);
        var samples = apu.DrainSamples();

        Assert.NotEmpty(samples);
        Assert.All(samples, sample => Assert.True(sample > 0));
        Assert.Equal(0, apu.PendingSampleCount);
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
    public void DelayedFourStepIrqRepeatsWhenReadOnWrapCycle()
    {
        var apu = new ApuBus();
        apu.WriteRegister(0x4017, 0x00);

        for (var i = 0; i < 29_832; i++)
        {
            apu.Clock();
        }

        Assert.Equal(0x40, apu.ReadStatus() & 0x40);

        for (var i = 0; i < 4; i++)
        {
            apu.Clock();
        }

        Assert.Equal(0x40, apu.ReadStatus() & 0x40);
        Assert.Equal(0x00, apu.ReadStatus() & 0x40);
    }

    [Fact]
    public void DelayedFourStepIrqClearsNormallyAfterWrapCycle()
    {
        var apu = new ApuBus();
        apu.WriteRegister(0x4017, 0x00);

        for (var i = 0; i < 29_833; i++)
        {
            apu.Clock();
        }

        Assert.Equal(0x40, apu.ReadStatus() & 0x40);

        for (var i = 0; i < 4; i++)
        {
            apu.Clock();
        }

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

    private static Cartridge CreateCartridge(byte dmcSample = 0)
    {
        var rom = new byte[16 + 16 * 1024 + 8 * 1024];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = 1;
        rom[5] = 1;
        rom[16] = dmcSample;
        return INesRomLoader.Load(rom);
    }

    private static void ClockDmcTimerTicks(ApuBus apu, int ticks)
    {
        var cycles = 1 + (ticks - 1) * apu.Dmc.TimerPeriod;
        for (var i = 0; i < cycles; i++)
        {
            apu.Clock();
        }
    }

    private static void ClockDmcDmaReady(ApuBus apu)
    {
        apu.Clock();
        apu.Clock();
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
