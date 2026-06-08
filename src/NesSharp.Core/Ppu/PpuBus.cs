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
    private const ulong OpenBusDecayDots = 60UL * DotsPerScanline * ScanlinesPerFrame;

    private readonly Cartridge.Cartridge cartridge;
    private readonly byte[] nametableRam = new byte[2 * 1024];
    private readonly byte[] paletteRam = new byte[32];
    private readonly byte[] oam = new byte[256];
    private readonly byte[] registers = new byte[8];
    private bool nmiPending;
    private int nmiPollDelay;
    private bool suppressVblankSet;
    private byte oamAddress;
    private byte openBus;
    private ulong openBusHighUpdatedAt;
    private ulong openBusLowUpdatedAt;
    private byte readBuffer;
    private byte fineX;
    private ushort currentVramAddress;
    private ushort temporaryVramAddress;
    private bool writeToggle;
    private ulong vblankSetTotalDots;

    public PpuBus(Cartridge.Cartridge cartridge)
    {
        this.cartridge = cartridge;
    }

    public int Dot { get; private set; }

    public int Scanline { get; private set; }

    public ulong Frame { get; private set; }

    public ulong TotalDots { get; private set; }

    public event Action? NmiSuppressed;

    public byte ReadRegister(ushort address)
    {
        var register = address & 0x0007;
        if (register != 2)
        {
            return register switch
            {
                4 => ReadOamData(),
                7 => ReadPpuData(),
                _ => GetOpenBus()
            };
        }

        ApplyStatusReadVblankSuppression();
        var lowOpenBus = (byte)(GetOpenBus() & 0x1F);
        var status = (byte)((registers[2] & 0xE0) | lowOpenBus);
        SetOpenBus((byte)((registers[2] & 0xE0) | lowOpenBus), refreshLowBits: false);
        registers[2] = (byte)(registers[2] & ~VblankFlag);
        writeToggle = false;
        return status;
    }

    public void WriteRegister(ushort address, byte value)
    {
        var register = address & 0x0007;
        SetOpenBus(value);
        switch (register)
        {
            case 0:
            {
                var wasNmiEnabled = IsNmiEnabled;
                registers[0] = value;
                temporaryVramAddress = (ushort)((temporaryVramAddress & 0xF3FF) | ((value & 0x03) << 10));
                if (!wasNmiEnabled && IsNmiEnabled && IsVblankSet)
                {
                    nmiPending = true;
                    nmiPollDelay = 1;
                }

                break;
            }
            case 2:
                break;
            case 3:
                oamAddress = value;
                registers[register] = value;
                break;
            case 4:
                oam[oamAddress++] = value;
                registers[register] = value;
                break;
            case 5:
                WriteScroll(value);
                registers[register] = value;
                break;
            case 6:
                WriteAddress(value);
                registers[register] = value;
                break;
            case 7:
                WritePpuData(value);
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
        Array.Clear(nametableRam);
        Array.Clear(paletteRam);
        Array.Clear(oam);
        Dot = 0;
        Scanline = 0;
        Frame = 0;
        TotalDots = 0;
        nmiPending = false;
        nmiPollDelay = 0;
        suppressVblankSet = false;
        oamAddress = 0;
        openBus = 0;
        openBusHighUpdatedAt = 0;
        openBusLowUpdatedAt = 0;
        readBuffer = 0;
        fineX = 0;
        currentVramAddress = 0;
        temporaryVramAddress = 0;
        writeToggle = false;
        vblankSetTotalDots = 0;
    }

    public void Clock(int ppuCycles)
    {
        for (var i = 0; i < ppuCycles; i++)
        {
            if (Scanline == 261 && Dot == 339 && IsOddFrame && IsRenderingEnabled)
            {
                TotalDots++;
                Dot = 0;
                Scanline = 0;
                Frame++;
                continue;
            }

            TotalDots++;
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
                if (suppressVblankSet)
                {
                    suppressVblankSet = false;
                }
                else
                {
                    SetVblank();
                }
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

    public void WriteOamDmaByte(byte value)
    {
        oam[oamAddress++] = value;
    }

    private bool IsNmiEnabled => (registers[0] & NmiEnableFlag) != 0;

    private bool IsVblankSet => (registers[2] & VblankFlag) != 0;

    private bool IsRenderingEnabled => (registers[1] & 0x18) != 0;

    private bool IsOddFrame => (Frame & 1) != 0;

    private int VramIncrement => (registers[0] & 0x04) == 0 ? 1 : 32;

    private void SetVblank()
    {
        registers[2] |= VblankFlag;
        vblankSetTotalDots = TotalDots;
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
        suppressVblankSet = false;
    }

    private void ApplyStatusReadVblankSuppression()
    {
        if (Scanline == 241 && Dot == 0)
        {
            suppressVblankSet = true;
            nmiPending = false;
            nmiPollDelay = 0;
            NmiSuppressed?.Invoke();
            return;
        }

        if (IsVblankSet && TotalDots >= vblankSetTotalDots && TotalDots - vblankSetTotalDots <= 1)
        {
            nmiPending = false;
            nmiPollDelay = 0;
            NmiSuppressed?.Invoke();
        }
    }

    private byte ReadOamData()
    {
        var value = oam[oamAddress];
        value = (oamAddress & 0x03) == 0x02 ? (byte)(value & 0xE3) : value;
        SetOpenBus(value);
        return value;
    }

    private byte ReadPpuData()
    {
        var address = (ushort)(currentVramAddress & 0x3FFF);
        byte result;
        if (address >= 0x3F00)
        {
            result = (byte)((ReadMemory(address) & 0x3F) | (GetOpenBus() & 0xC0));
            readBuffer = ReadMemory((ushort)(address - 0x1000));
        }
        else
        {
            result = readBuffer;
            readBuffer = ReadMemory(address);
        }

        IncrementVramAddress();
        SetOpenBus(result);
        return result;
    }

    private void WritePpuData(byte value)
    {
        WriteMemory((ushort)(currentVramAddress & 0x3FFF), value);
        IncrementVramAddress();
    }

    private void WriteScroll(byte value)
    {
        if (!writeToggle)
        {
            fineX = (byte)(value & 0x07);
            temporaryVramAddress = (ushort)((temporaryVramAddress & 0x7FE0) | (value >> 3));
            writeToggle = true;
            return;
        }

        temporaryVramAddress = (ushort)((temporaryVramAddress & 0x0C1F) | ((value & 0x07) << 12) | ((value & 0xF8) << 2));
        writeToggle = false;
    }

    private void WriteAddress(byte value)
    {
        if (!writeToggle)
        {
            temporaryVramAddress = (ushort)((temporaryVramAddress & 0x00FF) | ((value & 0x3F) << 8));
            writeToggle = true;
            return;
        }

        temporaryVramAddress = (ushort)((temporaryVramAddress & 0x7F00) | value);
        currentVramAddress = temporaryVramAddress;
        writeToggle = false;
    }

    private void IncrementVramAddress()
    {
        currentVramAddress = (ushort)((currentVramAddress + VramIncrement) & 0x7FFF);
    }

    private byte ReadMemory(ushort address)
    {
        address = NormalizePpuAddress(address);
        return address switch
        {
            <= 0x1FFF => cartridge.PpuRead(address),
            >= 0x2000 and <= 0x2FFF => nametableRam[MapNametableAddress(address)],
            >= 0x3F00 and <= 0x3FFF => paletteRam[MapPaletteAddress(address)],
            _ => 0
        };
    }

    private void WriteMemory(ushort address, byte value)
    {
        address = NormalizePpuAddress(address);
        switch (address)
        {
            case <= 0x1FFF:
                cartridge.PpuWrite(address, value);
                break;
            case >= 0x2000 and <= 0x2FFF:
                nametableRam[MapNametableAddress(address)] = value;
                break;
            case >= 0x3F00 and <= 0x3FFF:
                paletteRam[MapPaletteAddress(address)] = value;
                break;
        }
    }

    private static ushort NormalizePpuAddress(ushort address)
    {
        address = (ushort)(address & 0x3FFF);
        if (address is >= 0x3000 and <= 0x3EFF)
        {
            address -= 0x1000;
        }

        return address;
    }

    private int MapNametableAddress(ushort address)
    {
        var offset = (address - 0x2000) & 0x0FFF;
        var table = offset / 0x0400;
        var inner = offset & 0x03FF;
        var physicalTable = cartridge.Header.MirroringMode switch
        {
            MirroringMode.Vertical => table & 0x01,
            MirroringMode.Horizontal => table >> 1,
            MirroringMode.FourScreen => table & 0x01,
            _ => table & 0x01
        };

        return physicalTable * 0x0400 + inner;
    }

    private static int MapPaletteAddress(ushort address)
    {
        var offset = (address - 0x3F00) & 0x001F;
        return offset switch
        {
            0x10 => 0x00,
            0x14 => 0x04,
            0x18 => 0x08,
            0x1C => 0x0C,
            _ => offset
        };
    }

    private byte GetOpenBus()
    {
        if (TotalDots - openBusLowUpdatedAt >= OpenBusDecayDots)
        {
            openBus = (byte)(openBus & 0xE0);
        }

        if (TotalDots - openBusHighUpdatedAt >= OpenBusDecayDots)
        {
            openBus = (byte)(openBus & 0x1F);
        }

        return openBus;
    }

    private void SetOpenBus(byte value, bool refreshLowBits = true)
    {
        openBus = refreshLowBits ? value : (byte)((openBus & 0x1F) | (value & 0xE0));
        openBusHighUpdatedAt = TotalDots;
        if (refreshLowBits)
        {
            openBusLowUpdatedAt = TotalDots;
        }
    }
}
