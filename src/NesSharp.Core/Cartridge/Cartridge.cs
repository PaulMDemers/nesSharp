namespace NesSharp.Core.Cartridge;

public sealed class Cartridge
{
    private ulong syntheticNotificationDot;

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

    public byte PpuRead(ushort address)
    {
        Mapper.NotifyPpuAddress(address, syntheticNotificationDot);
        syntheticNotificationDot += 16;
        return Mapper.PpuRead(address);
    }

    public byte PpuRead(ushort address, ulong ppuDot)
    {
        Mapper.NotifyPpuAddress(address, ppuDot);
        return Mapper.PpuRead(address);
    }

    public byte PpuPeek(ushort address) => Mapper.PpuPeek(address);

    public void PpuWrite(ushort address, byte value)
    {
        Mapper.NotifyPpuAddress(address, syntheticNotificationDot);
        syntheticNotificationDot += 16;
        Mapper.PpuWrite(address, value);
    }

    public void PpuWrite(ushort address, byte value, ulong ppuDot)
    {
        Mapper.NotifyPpuAddress(address, ppuDot);
        Mapper.PpuWrite(address, value);
    }

    public void NotifyPpuAddress(ushort address)
    {
        Mapper.NotifyPpuAddress(address, syntheticNotificationDot);
        syntheticNotificationDot += 16;
    }

    public void NotifyPpuAddress(ushort address, ulong ppuDot) => Mapper.NotifyPpuAddress(address, ppuDot);

    public void LoadSaveRam(ReadOnlySpan<byte> data) => Mapper.LoadSaveRam(data);
}
