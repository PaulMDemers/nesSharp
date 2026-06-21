namespace NesSharp.Core.Cartridge;

public sealed class Mapper1 : IMapper
{
    private const int PrgRamBankSize = 8 * 1024;
    private const int PrgBankSize = 16 * 1024;
    private const int ChrBankSize = 4 * 1024;
    private const byte ShiftRegisterReset = 0x10;

    private readonly CartridgeHeader header;
    private readonly byte[] prgRom;
    private readonly byte[] chrMemory;
    private readonly byte[] prgRam;
    private byte shiftRegister = ShiftRegisterReset;
    private byte control = 0x0C;
    private byte chrBank0;
    private byte chrBank1;
    private byte prgBank;

    public Mapper1(CartridgeHeader header, byte[] prgRom, byte[] chrMemory)
    {
        if (header.PrgRomBanks < 1)
        {
            throw new InvalidRomException("Mapper 1 requires at least one PRG ROM bank.");
        }

        this.header = header;
        this.prgRom = prgRom;
        this.chrMemory = chrMemory;
        prgRam = new byte[Math.Max(PrgRamBankSize, header.PrgRamSize)];
    }

    public MirroringMode CurrentMirroringMode
    {
        get
        {
            if (header.HasFourScreenVram)
            {
                return MirroringMode.FourScreen;
            }

            return (control & 0x03) switch
            {
                0 => MirroringMode.OneScreenLower,
                1 => MirroringMode.OneScreenUpper,
                2 => MirroringMode.Vertical,
                _ => MirroringMode.Horizontal
            };
        }
    }

    public ReadOnlySpan<byte> SaveRam => prgRam;

    public bool IsIrqPending => false;

    public byte CpuRead(ushort address)
    {
        return address switch
        {
            >= 0x6000 and <= 0x7FFF => IsPrgRamEnabled ? prgRam[(address - 0x6000) % prgRam.Length] : (byte)0,
            >= 0x8000 => prgRom[MapPrgRomAddress(address)],
            _ => 0
        };
    }

    public void CpuWrite(ushort address, byte value)
    {
        if (address is >= 0x6000 and <= 0x7FFF)
        {
            if (IsPrgRamEnabled)
            {
                prgRam[(address - 0x6000) % prgRam.Length] = value;
            }

            return;
        }

        if (address < 0x8000)
        {
            return;
        }

        if ((value & 0x80) != 0)
        {
            shiftRegister = ShiftRegisterReset;
            control |= 0x0C;
            return;
        }

        var isComplete = (shiftRegister & 0x01) != 0;
        shiftRegister = (byte)((shiftRegister >> 1) | ((value & 0x01) << 4));
        if (!isComplete)
        {
            return;
        }

        WriteInternalRegister(address, shiftRegister);
        shiftRegister = ShiftRegisterReset;
    }

    public byte PpuRead(ushort address)
    {
        return PpuPeek(address);
    }

    public byte PpuPeek(ushort address)
    {
        if (address > 0x1FFF)
        {
            return 0;
        }

        return chrMemory[MapChrAddress(address)];
    }

    public void PpuWrite(ushort address, byte value)
    {
        if (address > 0x1FFF || !header.UsesChrRam)
        {
            return;
        }

        chrMemory[MapChrAddress(address)] = value;
    }

    public void NotifyPpuAddress(ushort address, ulong ppuDot)
    {
    }

    public void LoadSaveRam(ReadOnlySpan<byte> data)
    {
        data[..Math.Min(data.Length, prgRam.Length)].CopyTo(prgRam);
    }

    private bool IsPrgRamEnabled => (prgBank & 0x10) == 0;

    private int PrgBankCount => Math.Max(1, prgRom.Length / PrgBankSize);

    private int ChrBankCount => Math.Max(1, chrMemory.Length / ChrBankSize);

    private bool UsesFourKilobyteChrBanks => (control & 0x10) != 0;

    private int PrgBankMode => (control >> 2) & 0x03;

    private void WriteInternalRegister(ushort address, byte value)
    {
        value = (byte)(value & 0x1F);
        switch ((address >> 13) & 0x03)
        {
            case 0:
                control = value;
                break;
            case 1:
                chrBank0 = value;
                break;
            case 2:
                chrBank1 = value;
                break;
            case 3:
                prgBank = value;
                break;
        }
    }

    private int MapPrgRomAddress(ushort address)
    {
        var offset = address & (PrgBankSize - 1);
        var bank = PrgBankMode switch
        {
            0 or 1 => ((prgBank & 0x0E) + (address >= 0xC000 ? 1 : 0)) % PrgBankCount,
            2 => address < 0xC000 ? 0 : (prgBank & 0x0F) % PrgBankCount,
            _ => address < 0xC000 ? (prgBank & 0x0F) % PrgBankCount : PrgBankCount - 1
        };

        return bank * PrgBankSize + offset;
    }

    private int MapChrAddress(ushort address)
    {
        var offset = address & (ChrBankSize - 1);
        int bank;
        if (UsesFourKilobyteChrBanks)
        {
            bank = address < 0x1000 ? chrBank0 : chrBank1;
        }
        else
        {
            bank = (chrBank0 & 0x1E) + (address >= 0x1000 ? 1 : 0);
        }

        return (bank % ChrBankCount) * ChrBankSize + offset;
    }
}
