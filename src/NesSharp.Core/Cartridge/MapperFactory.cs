namespace NesSharp.Core.Cartridge;

public static class MapperFactory
{
    public static IMapper Create(CartridgeHeader header, byte[] prgRom, byte[] chrMemory)
    {
        return header.MapperNumber switch
        {
            0 => new Mapper0(header, prgRom, chrMemory),
            1 => new Mapper1(header, prgRom, chrMemory),
            2 => new Mapper2(header, prgRom, chrMemory),
            3 => new Mapper3(header, prgRom, chrMemory),
            4 => new Mapper4(header, prgRom, chrMemory),
            7 => new Mapper7(header, prgRom, chrMemory),
            _ => throw new NotSupportedException($"Mapper {header.MapperNumber} is not implemented yet.")
        };
    }
}
