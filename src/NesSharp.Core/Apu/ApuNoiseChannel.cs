namespace NesSharp.Core.Apu;

public sealed class ApuNoiseChannel
{
    private static readonly ushort[] NoisePeriods =
    [
        4, 8, 16, 32, 64, 96, 128, 160,
        202, 254, 380, 508, 762, 1016, 2034, 4068
    ];

    private byte envelopeDivider;
    private byte envelopeDecayLevel;
    private bool envelopeStart;
    private ushort timerCounter;

    public bool Enabled { get; private set; }

    public bool LengthCounterHalt { get; private set; }

    public bool ConstantVolume { get; private set; }

    public byte EnvelopePeriod { get; private set; }

    public byte EnvelopeOutput => ConstantVolume ? EnvelopePeriod : envelopeDecayLevel;

    public bool ModeFlag { get; private set; }

    public byte PeriodIndex { get; private set; }

    public ushort TimerPeriod => NoisePeriods[PeriodIndex];

    public byte LengthCounter { get; private set; }

    public ushort ShiftRegister { get; private set; } = 1;

    public bool IsOutputMuted => LengthCounter == 0 || (ShiftRegister & 0x01) != 0;

    public byte OutputLevel => IsOutputMuted ? (byte)0 : EnvelopeOutput;

    public void Reset()
    {
        Enabled = false;
        LengthCounterHalt = false;
        ConstantVolume = false;
        EnvelopePeriod = 0;
        envelopeDivider = 0;
        envelopeDecayLevel = 0;
        envelopeStart = false;
        ModeFlag = false;
        PeriodIndex = 0;
        timerCounter = 0;
        LengthCounter = 0;
        ShiftRegister = 1;
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (!enabled)
        {
            LengthCounter = 0;
        }
    }

    public void WriteControl(byte value)
    {
        LengthCounterHalt = (value & 0x20) != 0;
        ConstantVolume = (value & 0x10) != 0;
        EnvelopePeriod = (byte)(value & 0x0F);
    }

    public void WritePeriod(byte value)
    {
        ModeFlag = (value & 0x80) != 0;
        PeriodIndex = (byte)(value & 0x0F);
    }

    public void WriteLength(byte value, ReadOnlySpan<byte> lengthCounterTable)
    {
        if (Enabled)
        {
            LengthCounter = lengthCounterTable[value >> 3];
        }

        envelopeStart = true;
    }

    public void ClockEnvelope()
    {
        if (envelopeStart)
        {
            envelopeStart = false;
            envelopeDecayLevel = 15;
            envelopeDivider = EnvelopePeriod;
            return;
        }

        if (envelopeDivider > 0)
        {
            envelopeDivider--;
            return;
        }

        envelopeDivider = EnvelopePeriod;
        if (envelopeDecayLevel > 0)
        {
            envelopeDecayLevel--;
        }
        else if (LengthCounterHalt)
        {
            envelopeDecayLevel = 15;
        }
    }

    public void ClockLengthCounter()
    {
        if (LengthCounter > 0 && !LengthCounterHalt)
        {
            LengthCounter--;
        }
    }

    public void ClockShiftRegister()
    {
        var tapBit = ModeFlag ? 6 : 1;
        var feedback = (ShiftRegister & 0x01) ^ ((ShiftRegister >> tapBit) & 0x01);
        ShiftRegister = (ushort)((ShiftRegister >> 1) | (feedback << 14));
    }

    public void ClockTimer()
    {
        if (timerCounter == 0)
        {
            timerCounter = TimerPeriod;
            ClockShiftRegister();
            return;
        }

        timerCounter--;
    }
}
