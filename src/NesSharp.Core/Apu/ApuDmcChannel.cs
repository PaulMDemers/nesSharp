namespace NesSharp.Core.Apu;

public sealed class ApuDmcChannel
{
    private static readonly ushort[] NtscPeriods =
    [
        428, 380, 340, 320, 286, 254, 226, 214,
        190, 160, 142, 128, 106, 84, 72, 54
    ];

    public bool IrqEnabled { get; private set; }

    public bool Loop { get; private set; }

    public byte RateIndex { get; private set; }

    public ushort TimerPeriod => NtscPeriods[RateIndex];

    public byte OutputLevel { get; private set; }

    public ushort SampleAddress { get; private set; } = 0xC000;

    public ushort CurrentAddress { get; private set; } = 0xC000;

    public ushort SampleLength { get; private set; } = 1;

    public ushort BytesRemaining { get; private set; }

    public bool InterruptFlag { get; private set; }

    public bool IsActive => BytesRemaining > 0;

    public void Reset()
    {
        IrqEnabled = false;
        Loop = false;
        RateIndex = 0;
        OutputLevel = 0;
        SampleAddress = 0xC000;
        CurrentAddress = 0xC000;
        SampleLength = 1;
        BytesRemaining = 0;
        InterruptFlag = false;
    }

    public void WriteControl(byte value)
    {
        IrqEnabled = (value & 0x80) != 0;
        Loop = (value & 0x40) != 0;
        RateIndex = (byte)(value & 0x0F);
        if (!IrqEnabled)
        {
            InterruptFlag = false;
        }
    }

    public void WriteDirectLoad(byte value)
    {
        OutputLevel = (byte)(value & 0x7F);
    }

    public void WriteSampleAddress(byte value)
    {
        SampleAddress = (ushort)(0xC000 + value * 64);
    }

    public void WriteSampleLength(byte value)
    {
        SampleLength = (ushort)(value * 16 + 1);
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (BytesRemaining == 0)
            {
                RestartSample();
            }
        }
        else
        {
            BytesRemaining = 0;
        }
    }

    public void ClearInterrupt()
    {
        InterruptFlag = false;
    }

    public void RestartSample()
    {
        CurrentAddress = SampleAddress;
        BytesRemaining = SampleLength;
    }

    public void MarkSampleByteRead()
    {
        if (BytesRemaining == 0)
        {
            return;
        }

        CurrentAddress = CurrentAddress == 0xFFFF ? (ushort)0x8000 : (ushort)(CurrentAddress + 1);
        BytesRemaining--;
        if (BytesRemaining == 0)
        {
            if (Loop)
            {
                RestartSample();
            }
            else if (IrqEnabled)
            {
                InterruptFlag = true;
            }
        }
    }
}
