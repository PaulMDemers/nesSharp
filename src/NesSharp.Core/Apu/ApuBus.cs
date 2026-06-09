namespace NesSharp.Core.Apu;

public sealed class ApuBus
{
    private const byte Pulse1Enable = 0x01;
    private const byte Pulse2Enable = 0x02;
    private const byte TriangleEnable = 0x04;
    private const byte NoiseEnable = 0x08;
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
    private bool frameInterrupt;
    private bool dmcInterrupt;

    private byte noiseLength;

    public ApuPulseChannel Pulse1 { get; } = new(onesComplementNegate: true);

    public ApuPulseChannel Pulse2 { get; } = new(onesComplementNegate: false);

    public ApuTriangleChannel Triangle { get; } = new();

    public byte FrameCounterControl => frameCounterControl;

    public bool IsFrameInterruptPending => frameInterrupt;

    public byte StatusEnable => statusEnable;

    public void Reset()
    {
        Pulse1.Reset();
        Pulse2.Reset();
        Triangle.Reset();
        WriteStatus(0);
        frameCounterControl = 0;
        frameCycle = 0;
        frameInterrupt = false;
        dmcInterrupt = false;
    }

    public void Clock()
    {
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

    public byte ReadStatus()
    {
        var value = (byte)(
            (Pulse1.LengthCounter > 0 ? Pulse1Enable : 0) |
            (Pulse2.LengthCounter > 0 ? Pulse2Enable : 0) |
            (Triangle.LengthCounter > 0 ? TriangleEnable : 0) |
            (noiseLength > 0 ? NoiseEnable : 0) |
            (frameInterrupt ? FrameInterruptStatus : 0) |
            (dmcInterrupt ? DmcInterruptStatus : 0));

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
            case 0x400F:
                LoadLengthCounter(ref noiseLength, value, NoiseEnable);
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

    private void WriteStatus(byte value)
    {
        statusEnable = (byte)(value & 0x1F);
        dmcInterrupt = false;
        Pulse1.SetEnabled((statusEnable & Pulse1Enable) != 0);
        Pulse2.SetEnabled((statusEnable & Pulse2Enable) != 0);
        Triangle.SetEnabled((statusEnable & TriangleEnable) != 0);

        if ((statusEnable & NoiseEnable) == 0)
        {
            noiseLength = 0;
        }
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
        Triangle.ClockLinearCounter();
    }

    private void ClockHalfFrame()
    {
        Pulse1.ClockLengthCounter();
        Pulse2.ClockLengthCounter();
        Triangle.ClockLengthCounter();
        Pulse1.ClockSweep();
        Pulse2.ClockSweep();
        ClockLengthCounter(ref noiseLength);
    }

    private static void ClockLengthCounter(ref byte counter)
    {
        if (counter > 0)
        {
            counter--;
        }
    }
}
