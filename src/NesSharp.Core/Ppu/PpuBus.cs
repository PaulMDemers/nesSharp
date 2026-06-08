using NesSharp.Core.Cartridge;

namespace NesSharp.Core.Ppu;

public sealed class PpuBus
{
    private const byte VblankFlag = 0x80;
    private const byte SpriteZeroHitFlag = 0x40;
    private const byte SpriteOverflowFlag = 0x20;
    private const byte NmiEnableFlag = 0x80;
    private const int DotsPerScanline = 341;
    private const int ScanlinesPerFrame = 262;

    private readonly Cartridge.Cartridge cartridge;
    private readonly byte[] registers = new byte[8];
    private bool nmiPending;
    private int nmiPollDelay;
    private bool writeToggle;

    public PpuBus(Cartridge.Cartridge cartridge)
    {
        this.cartridge = cartridge;
    }

    public int Dot { get; private set; }

    public int Scanline { get; private set; }

    public ulong Frame { get; private set; }

    public byte ReadRegister(ushort address)
    {
        var register = address & 0x0007;
        if (register != 2)
        {
            return registers[register];
        }

        var status = registers[2];
        registers[2] = (byte)(registers[2] & ~VblankFlag);
        writeToggle = false;
        return status;
    }

    public void WriteRegister(ushort address, byte value)
    {
        var register = address & 0x0007;
        switch (register)
        {
            case 0:
            {
                var wasNmiEnabled = IsNmiEnabled;
                registers[0] = value;
                if (!wasNmiEnabled && IsNmiEnabled && IsVblankSet)
                {
                    nmiPending = true;
                    nmiPollDelay = 1;
                }

                break;
            }
            case 2:
                break;
            case 5:
            case 6:
                writeToggle = !writeToggle;
                registers[register] = value;
                break;
            default:
                registers[register] = value;
                break;
        }
    }

    public void Reset()
    {
        Array.Clear(registers);
        Dot = 0;
        Scanline = 0;
        Frame = 0;
        nmiPending = false;
        nmiPollDelay = 0;
        writeToggle = false;
    }

    public void Clock(int ppuCycles)
    {
        for (var i = 0; i < ppuCycles; i++)
        {
            if (Scanline == 261 && Dot == 339 && IsOddFrame && IsRenderingEnabled)
            {
                Dot = 0;
                Scanline = 0;
                Frame++;
                continue;
            }

            Dot++;
            if (Dot >= DotsPerScanline)
            {
                Dot = 0;
                Scanline++;
                if (Scanline >= ScanlinesPerFrame)
                {
                    Scanline = 0;
                    Frame++;
                }
            }

            if (Scanline == 241 && Dot == 1)
            {
                SetVblank();
            }
            else if (Scanline == 261 && Dot == 1)
            {
                ClearRenderingFlags();
            }
        }
    }

    public bool PollNmi()
    {
        if (!nmiPending)
        {
            return false;
        }

        if (nmiPollDelay > 0)
        {
            nmiPollDelay--;
            return false;
        }

        nmiPending = false;
        return true;
    }

    public byte ReadPatternTable(ushort address) => cartridge.PpuRead(address);

    public void WritePatternTable(ushort address, byte value) => cartridge.PpuWrite(address, value);

    private bool IsNmiEnabled => (registers[0] & NmiEnableFlag) != 0;

    private bool IsVblankSet => (registers[2] & VblankFlag) != 0;

    private bool IsRenderingEnabled => (registers[1] & 0x18) != 0;

    private bool IsOddFrame => (Frame & 1) != 0;

    private void SetVblank()
    {
        registers[2] |= VblankFlag;
        if (IsNmiEnabled)
        {
            nmiPending = true;
        }
    }

    private void ClearRenderingFlags()
    {
        registers[2] = (byte)(registers[2] & ~(VblankFlag | SpriteZeroHitFlag | SpriteOverflowFlag));
        nmiPending = false;
        nmiPollDelay = 0;
    }
}
