namespace NesSharp.Core.Cartridge;

public static class INesRomLoader
{
    private const int HeaderSize = 16;
    private const int TrainerSize = 512;
    private const int PrgBankSize = 16 * 1024;
    private const int ChrBankSize = 8 * 1024;
    private const int DefaultChrRamSize = 8 * 1024;
    private const int DefaultPrgRamSize = 8 * 1024;

    public static Cartridge LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllBytes(path));
    }

    public static Cartridge Load(ReadOnlySpan<byte> rom)
    {
        if (rom.Length < HeaderSize)
        {
            throw new InvalidRomException("ROM is smaller than the 16-byte iNES header.");
        }

        if (rom[0] != 'N' || rom[1] != 'E' || rom[2] != 'S' || rom[3] != 0x1A)
        {
            throw new InvalidRomException("ROM does not start with the iNES magic bytes.");
        }

        var flags6 = rom[6];
        var flags7 = rom[7];
        var hasTrainer = (flags6 & 0b0000_0100) != 0;
        var hasFourScreenVram = (flags6 & 0b0000_1000) != 0;
        var mapperNumber = (flags6 >> 4) | (flags7 & 0xF0);
        var format = IsNes20(flags7) ? NesFileFormat.Nes20 : NesFileFormat.INes;
        var prgRomBanks = rom[4];
        var chrRomBanks = rom[5];
        var prgRomSize = prgRomBanks * PrgBankSize;
        var chrRomSize = chrRomBanks * ChrBankSize;
        var chrMemorySize = chrRomSize == 0 ? DefaultChrRamSize : chrRomSize;
        var prgRamSize = rom[8] == 0 ? DefaultPrgRamSize : rom[8] * DefaultPrgRamSize;

        var mirroringMode = hasFourScreenVram
            ? MirroringMode.FourScreen
            : (flags6 & 0b0000_0001) == 0
                ? MirroringMode.Horizontal
                : MirroringMode.Vertical;

        var offset = HeaderSize + (hasTrainer ? TrainerSize : 0);
        if (rom.Length < offset + prgRomSize + chrRomSize)
        {
            throw new InvalidRomException("ROM ended before the PRG/CHR data declared by its header.");
        }

        var prgRom = rom.Slice(offset, prgRomSize).ToArray();
        offset += prgRomSize;

        var chrMemory = new byte[chrMemorySize];
        if (chrRomSize > 0)
        {
            rom.Slice(offset, chrRomSize).CopyTo(chrMemory);
        }

        var header = new CartridgeHeader(
            format,
            prgRomBanks,
            chrRomBanks,
            mapperNumber,
            mirroringMode,
            (flags6 & 0b0000_0010) != 0,
            hasTrainer,
            hasFourScreenVram,
            prgRomSize,
            chrRomSize,
            prgRamSize);

        var mapper = MapperFactory.Create(header, prgRom, chrMemory);
        return new Cartridge(header, prgRom, chrMemory, mapper);
    }

    private static bool IsNes20(byte flags7) => (flags7 & 0b0000_1100) == 0b0000_1000;
}

