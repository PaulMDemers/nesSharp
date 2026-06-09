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

    private byte pulse1Length;
    private byte pulse2Length;
    private byte triangleLength;
    private byte noiseLength;

    public byte FrameCounterControl => frameCounterControl;

    public bool IsFrameInterruptPending => frameInterrupt;

    public byte StatusEnable => statusEnable;

    public void Reset()
    {
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
            (pulse1Length > 0 ? Pulse1Enable : 0) |
            (pulse2Length > 0 ? Pulse2Enable : 0) |
            (triangleLength > 0 ? TriangleEnable : 0) |
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
            case 0x4003:
                LoadLengthCounter(ref pulse1Length, value, Pulse1Enable);
                break;
            case 0x4007:
                LoadLengthCounter(ref pulse2Length, value, Pulse2Enable);
                break;
            case 0x400B:
                LoadLengthCounter(ref triangleLength, value, TriangleEnable);
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
        if ((statusEnable & Pulse1Enable) == 0)
        {
            pulse1Length = 0;
        }

        if ((statusEnable & Pulse2Enable) == 0)
        {
            pulse2Length = 0;
        }

        if ((statusEnable & TriangleEnable) == 0)
        {
            triangleLength = 0;
        }

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
        // Envelope and triangle linear counter clocks land here once those units exist.
    }

    private void ClockHalfFrame()
    {
        ClockLengthCounter(ref pulse1Length);
        ClockLengthCounter(ref pulse2Length);
        ClockLengthCounter(ref triangleLength);
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
