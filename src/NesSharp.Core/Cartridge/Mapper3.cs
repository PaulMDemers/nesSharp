namespace NesSharp.Core.Cartridge;

public sealed class Mapper3 : IMapper
{
    private const int PrgBankSize = 16 * 1024;
    private const int ChrBankSize = 8 * 1024;

    private readonly CartridgeHeader header;
    private readonly byte[] prgRom;
    private readonly byte[] chrMemory;
    private readonly byte[] prgRam;
    private int chrBank;

    public Mapper3(CartridgeHeader header, byte[] prgRom, byte[] chrMemory)
    {
        if (header.PrgRomBanks is not (1 or 2))
        {
            throw new InvalidRomException($"Mapper 3 supports 1 or 2 PRG banks, got {header.PrgRomBanks}.");
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
            chrBank = value % Math.Max(1, chrMemory.Length / ChrBankSize);
        }
    }

    public byte PpuRead(ushort address)
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

    public void NotifyPpuAddress(ushort address)
    {
    }

    public void LoadSaveRam(ReadOnlySpan<byte> data)
    {
        data[..Math.Min(data.Length, prgRam.Length)].CopyTo(prgRam);
    }

    private int MapPrgRomAddress(ushort address)
    {
        var offset = address - 0x8000;
        return header.PrgRomBanks == 1 ? offset % PrgBankSize : offset;
    }

    private int MapChrAddress(ushort address)
    {
        return chrBank * ChrBankSize + address % ChrBankSize;
    }
}
