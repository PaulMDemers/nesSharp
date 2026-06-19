namespace NesSharp.Core.Cartridge;

public interface IMapper
{
    MirroringMode CurrentMirroringMode { get; }

    ReadOnlySpan<byte> SaveRam { get; }

    bool IsIrqPending { get; }

    byte CpuRead(ushort address);

    void CpuWrite(ushort address, byte value);

    byte PpuRead(ushort address);

    byte PpuPeek(ushort address);

    void PpuWrite(ushort address, byte value);

    void NotifyPpuAddress(ushort address);

    void LoadSaveRam(ReadOnlySpan<byte> data);
}
