namespace NesSharp.Core.Apu;

public sealed class ApuDmcChannel
{
    private static readonly ushort[] NtscPeriods =
    [
        428, 380, 340, 320, 286, 254, 226, 214,
        190, 160, 142, 128, 106, 84, 72, 54
    ];

    private ushort timerCounter;
    private byte? sampleBuffer;
    private byte shiftRegister;
    private byte bitsRemaining = 8;
    private bool silence = true;
    private ushort pendingSampleAddress;
    private bool sampleFetchPending;
    private byte sampleFetchDelayCycles;

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

    public bool IsSampleFetchPending => sampleFetchPending;

    public bool IsSampleFetchReady => sampleFetchPending && sampleFetchDelayCycles == 0;

    public ushort PendingSampleAddress => pendingSampleAddress;

    public bool SampleBufferEmpty => sampleBuffer is null;

    public byte BitsRemaining => bitsRemaining;

    public bool Silence => silence;

    public void Reset()
    {
        IrqEnabled = false;
        Loop = false;
        RateIndex = 0;
        timerCounter = 0;
        OutputLevel = 0;
        SampleAddress = 0xC000;
        CurrentAddress = 0xC000;
        SampleLength = 1;
        BytesRemaining = 0;
        InterruptFlag = false;
        sampleBuffer = null;
        shiftRegister = 0;
        bitsRemaining = 8;
        silence = true;
        pendingSampleAddress = 0;
        sampleFetchPending = false;
        sampleFetchDelayCycles = 0;
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

            RequestSampleFetch();
        }
        else
        {
            BytesRemaining = 0;
            sampleFetchPending = false;
            sampleFetchDelayCycles = 0;
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
        if (sampleFetchPending)
        {
            CompleteSampleFetch(0);
        }
        else
        {
            CompleteSampleByteRead();
        }
    }

    public void CompleteSampleFetch(byte value)
    {
        if (!sampleFetchPending)
        {
            return;
        }

        sampleFetchPending = false;
        sampleFetchDelayCycles = 0;
        sampleBuffer = value;
        CompleteSampleByteRead();
    }

    public void ClockDmaDelay()
    {
        if (sampleFetchDelayCycles > 0)
        {
            sampleFetchDelayCycles--;
        }
    }

    public void ClockTimer()
    {
        if (timerCounter == 0)
        {
            timerCounter = (ushort)(TimerPeriod - 1);
            ClockOutputUnit();
            return;
        }

        timerCounter--;
    }

    private void ClockOutputUnit()
    {
        if (!silence)
        {
            if ((shiftRegister & 0x01) == 0)
            {
                if (OutputLevel >= 2)
                {
                    OutputLevel -= 2;
                }
            }
            else if (OutputLevel <= 125)
            {
                OutputLevel += 2;
            }
        }

        shiftRegister >>= 1;
        bitsRemaining--;
        if (bitsRemaining == 0)
        {
            StartOutputCycle();
        }
    }

    private void StartOutputCycle()
    {
        bitsRemaining = 8;
        if (sampleBuffer is null)
        {
            silence = true;
            RequestSampleFetch();
            return;
        }

        silence = false;
        shiftRegister = sampleBuffer.Value;
        sampleBuffer = null;
        RequestSampleFetch();
    }

    private void RequestSampleFetch()
    {
        if (sampleFetchPending || sampleBuffer is not null || BytesRemaining == 0)
        {
            return;
        }

        pendingSampleAddress = CurrentAddress;
        sampleFetchPending = true;
        sampleFetchDelayCycles = 2;
    }

    private void CompleteSampleByteRead()
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
