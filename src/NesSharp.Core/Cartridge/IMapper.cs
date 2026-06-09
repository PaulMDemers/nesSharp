namespace NesSharp.Core.Cartridge;

public interface IMapper
{
    MirroringMode CurrentMirroringMode { get; }

    ReadOnlySpan<byte> SaveRam { get; }

    byte CpuRead(ushort address);

    void CpuWrite(ushort address, byte value);

    byte PpuRead(ushort address);

    void PpuWrite(ushort address, byte value);

    void LoadSaveRam(ReadOnlySpan<byte> data);
}
