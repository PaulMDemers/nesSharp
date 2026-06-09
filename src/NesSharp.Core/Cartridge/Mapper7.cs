namespace NesSharp.Core.Cartridge;

public sealed class Mapper7 : IMapper
{
    private const int PrgBankSize = 32 * 1024;

    private readonly CartridgeHeader header;
    private readonly byte[] prgRom;
    private readonly byte[] chrMemory;
    private readonly byte[] prgRam;
    private int prgBank;
    private MirroringMode mirroringMode = MirroringMode.OneScreenLower;

    public Mapper7(CartridgeHeader header, byte[] prgRom, byte[] chrMemory)
    {
        if (header.PrgRomBanks < 2)
        {
            throw new InvalidRomException($"Mapper 7 requires at least 2 PRG banks, got {header.PrgRomBanks}.");
        }

        this.header = header;
        this.prgRom = prgRom;
        this.chrMemory = chrMemory;
        prgRam = new byte[header.PrgRamSize];
    }

    public MirroringMode CurrentMirroringMode => header.HasFourScreenVram ? MirroringMode.FourScreen : mirroringMode;

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

        if (address < 0x8000)
        {
            return;
        }

        prgBank = (value & 0x07) % Math.Max(1, prgRom.Length / PrgBankSize);
        mirroringMode = (value & 0x10) == 0 ? MirroringMode.OneScreenLower : MirroringMode.OneScreenUpper;
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

    private int MapPrgRomAddress(ushort address)
    {
        return prgBank * PrgBankSize + (address - 0x8000);
    }
}
