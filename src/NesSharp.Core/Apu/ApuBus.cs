namespace NesSharp.Core.Apu;

public sealed class ApuBus
{
    private const double CpuClockRate = 1_789_773.0;
    public const int OutputSampleRate = 44_100;
    private const double SampleRate = OutputSampleRate;
    private const byte Pulse1Enable = 0x01;
    private const byte Pulse2Enable = 0x02;
    private const byte TriangleEnable = 0x04;
    private const byte NoiseEnable = 0x08;
    private const byte DmcEnable = 0x10;
    private const byte FrameInterruptStatus = 0x40;
    private const byte DmcInterruptStatus = 0x80;
    private const int FourStepFrameCycles = 29_829;

    private static readonly byte[] LengthCounterTable =
    [
        10, 254, 20, 2, 40, 4, 80, 6,
        160, 8, 60, 10, 14, 12, 26, 14,
        12, 16, 24, 18, 48, 20, 96, 22,
        192, 24, 72, 26, 16, 28, 32, 30
    ];

    private byte statusEnable;
    private byte frameCounterControl;
    private int frameCycle;
    private ulong cpuCycle;
    private double sampleAccumulator;
    private bool frameInterrupt;
    private readonly List<float> samples = [];

    public ApuPulseChannel Pulse1 { get; } = new(onesComplementNegate: true);

    public ApuPulseChannel Pulse2 { get; } = new(onesComplementNegate: false);

    public ApuTriangleChannel Triangle { get; } = new();

    public ApuNoiseChannel Noise { get; } = new();

    public ApuDmcChannel Dmc { get; } = new();

    public byte FrameCounterControl => frameCounterControl;

    public bool IsFrameInterruptPending => frameInterrupt;

    public bool IsDmcInterruptPending => Dmc.InterruptFlag;

    public byte StatusEnable => statusEnable;

    public int PendingSampleCount => samples.Count;

    public bool IsDmcDmaPending => Dmc.IsSampleFetchPending;

    public ushort PendingDmcDmaAddress => Dmc.PendingSampleAddress;

    public int PendingDmcDmaCycles => Dmc.PendingSampleFetchCycles;

    public void CompleteDmcDma(byte value)
    {
        Dmc.CompleteSampleFetch(value);
    }

    public void Reset()
    {
        Pulse1.Reset();
        Pulse2.Reset();
        Triangle.Reset();
        Noise.Reset();
        Dmc.Reset();
        WriteStatus(0);
        frameCounterControl = 0;
        frameCycle = 0;
        cpuCycle = 0;
        sampleAccumulator = 0;
        frameInterrupt = false;
        samples.Clear();
    }

    public void Clock()
    {
        cpuCycle++;
        ClockChannelTimers();
        MaybeEmitSample();

        frameCycle++;
        if (frameCycle == 7_457 || frameCycle == 14_913 || frameCycle == 22_371)
        {
            ClockQuarterFrame();
        }

        if (frameCycle == 14_913)
        {
            ClockHalfFrame();
        }

        if (IsFiveStepMode)
        {
            if (frameCycle == 37_281)
            {
                ClockQuarterFrame();
                ClockHalfFrame();
                frameCycle = 0;
            }

            return;
        }

        if (frameCycle != FourStepFrameCycles)
        {
            return;
        }

        ClockQuarterFrame();
        ClockHalfFrame();
        if (!IsFrameInterruptInhibited)
        {
            frameInterrupt = true;
        }

        frameCycle = 0;
    }

    public float[] DrainSamples()
    {
        var drained = samples.ToArray();
        samples.Clear();
        return drained;
    }

    public byte ReadStatus()
    {
        var value = (byte)(
            (Pulse1.LengthCounter > 0 ? Pulse1Enable : 0) |
            (Pulse2.LengthCounter > 0 ? Pulse2Enable : 0) |
            (Triangle.LengthCounter > 0 ? TriangleEnable : 0) |
            (Noise.LengthCounter > 0 ? NoiseEnable : 0) |
            (Dmc.IsActive ? DmcEnable : 0) |
            (frameInterrupt ? FrameInterruptStatus : 0) |
            (Dmc.InterruptFlag ? DmcInterruptStatus : 0));

        frameInterrupt = false;
        return value;
    }

    public void WriteRegister(ushort address, byte value)
    {
        switch (address)
        {
            case 0x4000:
                Pulse1.WriteControl(value);
                break;
            case 0x4001:
                Pulse1.WriteSweep(value);
                break;
            case 0x4002:
                Pulse1.WriteTimerLow(value);
                break;
            case 0x4003:
                Pulse1.WriteTimerHigh(value, LengthCounterTable);
                break;
            case 0x4004:
                Pulse2.WriteControl(value);
                break;
            case 0x4005:
                Pulse2.WriteSweep(value);
                break;
            case 0x4006:
                Pulse2.WriteTimerLow(value);
                break;
            case 0x4007:
                Pulse2.WriteTimerHigh(value, LengthCounterTable);
                break;
            case 0x4008:
                Triangle.WriteLinearCounter(value);
                break;
            case 0x400A:
                Triangle.WriteTimerLow(value);
                break;
            case 0x400B:
                Triangle.WriteTimerHigh(value, LengthCounterTable);
                break;
            case 0x400C:
                Noise.WriteControl(value);
                break;
            case 0x400E:
                Noise.WritePeriod(value);
                break;
            case 0x400F:
                Noise.WriteLength(value, LengthCounterTable);
                break;
            case 0x4010:
                Dmc.WriteControl(value);
                break;
            case 0x4011:
                Dmc.WriteDirectLoad(value);
                break;
            case 0x4012:
                Dmc.WriteSampleAddress(value);
                break;
            case 0x4013:
                Dmc.WriteSampleLength(value);
                break;
            case 0x4015:
                WriteStatus(value);
                break;
            case 0x4017:
                WriteFrameCounter(value);
                break;
        }
    }

    private bool IsFiveStepMode => (frameCounterControl & 0x80) != 0;

    private bool IsFrameInterruptInhibited => (frameCounterControl & 0x40) != 0;

    private void ClockChannelTimers()
    {
        if ((cpuCycle & 0x01) == 0)
        {
            Pulse1.ClockTimer();
            Pulse2.ClockTimer();
            Noise.ClockTimer();
        }

        Dmc.ClockTimer();
        Triangle.ClockTimer();
    }

    private void MaybeEmitSample()
    {
        sampleAccumulator += SampleRate;
        if (sampleAccumulator < CpuClockRate)
        {
            return;
        }

        sampleAccumulator -= CpuClockRate;
        samples.Add(MixSample());
    }

    public float MixSample()
    {
        var pulse = Pulse1.OutputLevel + Pulse2.OutputLevel;
        var pulseOut = pulse == 0 ? 0 : 95.88 / (8128.0 / pulse + 100);
        var tndInput = Triangle.OutputLevel / 8227.0 + Noise.OutputLevel / 12241.0 + Dmc.OutputLevel / 22638.0;
        var tndOut = tndInput == 0 ? 0 : 159.79 / (1 / tndInput + 100);
        return (float)(pulseOut + tndOut);
    }

    private void WriteStatus(byte value)
    {
        statusEnable = (byte)(value & 0x1F);
        Pulse1.SetEnabled((statusEnable & Pulse1Enable) != 0);
        Pulse2.SetEnabled((statusEnable & Pulse2Enable) != 0);
        Triangle.SetEnabled((statusEnable & TriangleEnable) != 0);
        Noise.SetEnabled((statusEnable & NoiseEnable) != 0);
        Dmc.ClearInterrupt();
        Dmc.SetEnabled((statusEnable & DmcEnable) != 0);
    }

    private void WriteFrameCounter(byte value)
    {
        frameCounterControl = (byte)(value & 0xC0);
        frameCycle = 0;
        if (IsFrameInterruptInhibited)
        {
            frameInterrupt = false;
        }

        if (IsFiveStepMode)
        {
            ClockQuarterFrame();
            ClockHalfFrame();
        }
    }

    private void LoadLengthCounter(ref byte counter, byte value, byte enableMask)
    {
        if ((statusEnable & enableMask) != 0)
        {
            counter = LengthCounterTable[value >> 3];
        }
    }

    private void ClockQuarterFrame()
    {
        Pulse1.ClockEnvelope();
        Pulse2.ClockEnvelope();
        Noise.ClockEnvelope();
        Triangle.ClockLinearCounter();
    }

    private void ClockHalfFrame()
    {
        Pulse1.ClockLengthCounter();
        Pulse2.ClockLengthCounter();
        Triangle.ClockLengthCounter();
        Pulse1.ClockSweep();
        Pulse2.ClockSweep();
        Noise.ClockLengthCounter();
    }
}
