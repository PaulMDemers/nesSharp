namespace NesSharp.Core.Cartridge;

public sealed class Cartridge
{
    public Cartridge(CartridgeHeader header, byte[] prgRom, byte[] chrMemory, IMapper mapper)
    {
        Header = header;
        PrgRom = prgRom;
        ChrMemory = chrMemory;
        Mapper = mapper;
    }

    public CartridgeHeader Header { get; }

    public byte[] PrgRom { get; }

    public byte[] ChrMemory { get; }

    public IMapper Mapper { get; }

    public byte CpuRead(ushort address) => Mapper.CpuRead(address);

    public void CpuWrite(ushort address, byte value) => Mapper.CpuWrite(address, value);

    public byte PpuRead(ushort address) => Mapper.PpuRead(address);

    public void PpuWrite(ushort address, byte value) => Mapper.PpuWrite(address, value);
}

