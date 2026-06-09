namespace NesSharp.Core.Cartridge;

public sealed class Mapper0 : IMapper
{
    private readonly CartridgeHeader header;
    private readonly byte[] prgRom;
    private readonly byte[] chrMemory;
    private readonly byte[] prgRam;

    public Mapper0(CartridgeHeader header, byte[] prgRom, byte[] chrMemory)
    {
        if (header.PrgRomBanks is not (1 or 2))
        {
            throw new InvalidRomException($"Mapper 0 supports 1 or 2 PRG banks, got {header.PrgRomBanks}.");
        }

        this.header = header;
        this.prgRom = prgRom;
        this.chrMemory = chrMemory;
        prgRam = new byte[header.PrgRamSize];
    }

    public MirroringMode CurrentMirroringMode => header.MirroringMode;

    public ReadOnlySpan<byte> SaveRam => prgRam;

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
        }
    }

    public byte PpuRead(ushort address)
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

    public void LoadSaveRam(ReadOnlySpan<byte> data)
    {
        data[..Math.Min(data.Length, prgRam.Length)].CopyTo(prgRam);
    }

    private int MapPrgRomAddress(ushort address)
    {
        var offset = address - 0x8000;
        return header.PrgRomBanks == 1 ? offset % 0x4000 : offset;
    }
}
