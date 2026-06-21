namespace NesSharp.Core.Cartridge;

public sealed class Mapper2 : IMapper
{
    private const int PrgBankSize = 16 * 1024;

    private readonly CartridgeHeader header;
    private readonly byte[] prgRom;
    private readonly byte[] chrMemory;
    private readonly byte[] prgRam;
    private int prgBank;

    public Mapper2(CartridgeHeader header, byte[] prgRom, byte[] chrMemory)
    {
        if (header.PrgRomBanks < 2)
        {
            throw new InvalidRomException($"Mapper 2 requires at least 2 PRG banks, got {header.PrgRomBanks}.");
        }

        this.header = header;
        this.prgRom = prgRom;
        this.chrMemory = chrMemory;
        prgRam = new byte[header.PrgRamSize];
    }

    public MirroringMode CurrentMirroringMode => header.MirroringMode;

    public ReadOnlySpan<byte> SaveRam => prgRam;

    public bool IsIrqPending => false;

    public byte CpuRead(ushort address)
    {
        return address switch
        {
            >= 0x6000 and <= 0x7FFF => prgRam[(address - 0x6000) % prgRam.Length],
            >= 0x8000 => prgRom[MapPrgRomAddress(address)],
            _ => 0
        };
    }

    public void CpuWrite(ushort address, byte value)
    {
        if (address is >= 0x6000 and <= 0x7FFF)
        {
            prgRam[(address - 0x6000) % prgRam.Length] = value;
            return;
        }

        if (address >= 0x8000)
        {
            prgBank = value % header.PrgRomBanks;
        }
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

        return chrMemory[address % chrMemory.Length];
    }

    public void PpuWrite(ushort address, byte value)
    {
        if (address > 0x1FFF || !header.UsesChrRam)
        {
            return;
        }

        chrMemory[address % chrMemory.Length] = value;
    }

    public void NotifyPpuAddress(ushort address, ulong ppuDot)
    {
    }

    public void LoadSaveRam(ReadOnlySpan<byte> data)
    {
        data[..Math.Min(data.Length, prgRam.Length)].CopyTo(prgRam);
    }

    private int MapPrgRomAddress(ushort address)
    {
        var offset = address & (PrgBankSize - 1);
        var bank = address < 0xC000 ? prgBank : header.PrgRomBanks - 1;
        return bank * PrgBankSize + offset;
    }
}
