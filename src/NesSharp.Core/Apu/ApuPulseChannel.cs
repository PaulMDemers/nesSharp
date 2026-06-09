namespace NesSharp.Core.Apu;

public sealed class ApuPulseChannel
{
    private readonly bool onesComplementNegate;
    private byte envelopeDivider;
    private byte envelopeDecayLevel;
    private bool envelopeStart;
    private byte sweepDivider;
    private bool sweepReload;

    public ApuPulseChannel(bool onesComplementNegate)
    {
        this.onesComplementNegate = onesComplementNegate;
    }

    public bool Enabled { get; private set; }

    public byte DutyCycle { get; private set; }

    public bool LengthCounterHalt { get; private set; }

    public bool ConstantVolume { get; private set; }

    public byte EnvelopePeriod { get; private set; }

    public byte EnvelopeOutput => ConstantVolume ? EnvelopePeriod : envelopeDecayLevel;

    public bool SweepEnabled { get; private set; }

    public byte SweepPeriod { get; private set; }

    public bool SweepNegate { get; private set; }

    public byte SweepShift { get; private set; }

    public ushort TimerPeriod { get; private set; }

    public byte LengthCounter { get; private set; }

    public int SweepTargetPeriod
    {
        get
        {
            var change = TimerPeriod >> SweepShift;
            return SweepNegate
                ? TimerPeriod - change - (onesComplementNegate ? 1 : 0)
                : TimerPeriod + change;
        }
    }

    public bool SweepMuted => TimerPeriod < 8 || SweepTargetPeriod > 0x7FF;

    public void Reset()
    {
        Enabled = false;
        DutyCycle = 0;
        LengthCounterHalt = false;
        ConstantVolume = false;
        EnvelopePeriod = 0;
        envelopeDivider = 0;
        envelopeDecayLevel = 0;
        envelopeStart = false;
        SweepEnabled = false;
        SweepPeriod = 0;
        SweepNegate = false;
        SweepShift = 0;
        sweepDivider = 0;
        sweepReload = false;
        TimerPeriod = 0;
        LengthCounter = 0;
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
        DutyCycle = (byte)(value >> 6);
        LengthCounterHalt = (value & 0x20) != 0;
        ConstantVolume = (value & 0x10) != 0;
        EnvelopePeriod = (byte)(value & 0x0F);
    }

    public void WriteSweep(byte value)
    {
        SweepEnabled = (value & 0x80) != 0;
        SweepPeriod = (byte)((value >> 4) & 0x07);
        SweepNegate = (value & 0x08) != 0;
        SweepShift = (byte)(value & 0x07);
        sweepReload = true;
    }

    public void WriteTimerLow(byte value)
    {
        TimerPeriod = (ushort)((TimerPeriod & 0x0700) | value);
    }

    public void WriteTimerHigh(byte value, ReadOnlySpan<byte> lengthCounterTable)
    {
        TimerPeriod = (ushort)((TimerPeriod & 0x00FF) | ((value & 0x07) << 8));
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

    public void ClockSweep()
    {
        if (sweepDivider == 0 && SweepEnabled && SweepShift != 0 && !SweepMuted)
        {
            TimerPeriod = (ushort)SweepTargetPeriod;
        }

        if (sweepDivider == 0 || sweepReload)
        {
            sweepDivider = SweepPeriod;
            sweepReload = false;
        }
        else
        {
            sweepDivider--;
        }
    }
}
