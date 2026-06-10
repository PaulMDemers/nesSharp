namespace NesSharp.Core.Apu;

public sealed class ApuTriangleChannel
{
    private static readonly byte[] Sequence =
    [
        15, 14, 13, 12, 11, 10, 9, 8,
        7, 6, 5, 4, 3, 2, 1, 0,
        0, 1, 2, 3, 4, 5, 6, 7,
        8, 9, 10, 11, 12, 13, 14, 15
    ];

    private bool linearCounterReload;
    private ushort timerCounter;
    private byte sequenceStep;

    public bool Enabled { get; private set; }

    public bool ControlFlag { get; private set; }

    public byte LinearCounterReloadValue { get; private set; }

    public byte LinearCounter { get; private set; }

    public ushort TimerPeriod { get; private set; }

    public byte LengthCounter { get; private set; }

    public bool IsSequencerClocking => LengthCounter > 0 && LinearCounter > 0;

    public byte OutputLevel => IsSequencerClocking ? Sequence[sequenceStep] : (byte)0;

    public void Reset()
    {
        Enabled = false;
        ControlFlag = false;
        LinearCounterReloadValue = 0;
        LinearCounter = 0;
        linearCounterReload = false;
        TimerPeriod = 0;
        timerCounter = 0;
        sequenceStep = 0;
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

    public void WriteLinearCounter(byte value)
    {
        ControlFlag = (value & 0x80) != 0;
        LinearCounterReloadValue = (byte)(value & 0x7F);
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

        linearCounterReload = true;
    }

    public void ClockTimer()
    {
        if (timerCounter == 0)
        {
            timerCounter = TimerPeriod;
            if (IsSequencerClocking && TimerPeriod > 1)
            {
                sequenceStep = (byte)((sequenceStep + 1) & 0x1F);
            }

            return;
        }

        timerCounter--;
    }

    public void ClockLinearCounter()
    {
        if (linearCounterReload)
        {
            LinearCounter = LinearCounterReloadValue;
        }
        else if (LinearCounter > 0)
        {
            LinearCounter--;
        }

        if (!ControlFlag)
        {
            linearCounterReload = false;
        }
    }

    public void ClockLengthCounter()
    {
        if (LengthCounter > 0 && !ControlFlag)
        {
            LengthCounter--;
        }
    }
}
