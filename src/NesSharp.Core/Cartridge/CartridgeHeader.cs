namespace NesSharp.Core.Cartridge;

public sealed record CartridgeHeader(
    NesFileFormat Format,
    int PrgRomBanks,
    int ChrRomBanks,
    int MapperNumber,
    MirroringMode MirroringMode,
    bool HasBatteryBackedRam,
    bool HasTrainer,
    bool HasFourScreenVram,
    int PrgRomSize,
    int ChrRomSize,
    int PrgRamSize)
{
    public bool UsesChrRam => ChrRomBanks == 0;
}

