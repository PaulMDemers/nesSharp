namespace NesSharp.Core.Input;

public sealed class StandardController
{
    private ControllerButton state;
    private byte shiftRegister;
    private bool strobe;

    public ControllerButton State
    {
        get => state;
        set
        {
            state = value;
            if (strobe)
            {
                shiftRegister = (byte)state;
            }
        }
    }

    public void WriteStrobe(bool enabled)
    {
        strobe = enabled;
        if (strobe)
        {
            shiftRegister = (byte)state;
        }
    }

    public byte Read()
    {
        if (strobe)
        {
            shiftRegister = (byte)state;
        }

        var value = (byte)(0x40 | (shiftRegister & 0x01));
        if (!strobe)
        {
            shiftRegister = (byte)((shiftRegister >> 1) | 0x80);
        }

        return value;
    }
}

