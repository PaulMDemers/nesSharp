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

    public MirroringMode CurrentMirroringMode => Mapper.CurrentMirroringMode;

    public bool IsIrqPending => Mapper.IsIrqPending;

    public bool HasBatteryBackedSaveRam => Header.HasBatteryBackedRam && SaveRam.Length > 0;

    public ReadOnlySpan<byte> SaveRam => Mapper.SaveRam;

    public byte CpuRead(ushort address) => Mapper.CpuRead(address);

    public void CpuWrite(ushort address, byte value) => Mapper.CpuWrite(address, value);

    public byte PpuRead(ushort address) => Mapper.PpuRead(address);

    public byte PpuPeek(ushort address) => Mapper.PpuPeek(address);

    public void PpuWrite(ushort address, byte value) => Mapper.PpuWrite(address, value);

    public void NotifyPpuAddress(ushort address) => Mapper.NotifyPpuAddress(address);

    public void LoadSaveRam(ReadOnlySpan<byte> data) => Mapper.LoadSaveRam(data);
}
