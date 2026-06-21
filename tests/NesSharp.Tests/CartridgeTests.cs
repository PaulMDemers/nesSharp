using NesSharp.Core.Cartridge;

namespace NesSharp.Tests;

public sealed class CartridgeTests
{
    [Fact]
    public void LoadsNestestHeader()
    {
        var path = TestRomPath("other", "nestest.nes");

        var cartridge = INesRomLoader.LoadFile(path);

        Assert.Equal(NesFileFormat.INes, cartridge.Header.Format);
        Assert.Equal(0, cartridge.Header.MapperNumber);
        Assert.Equal(1, cartridge.Header.PrgRomBanks);
        Assert.Equal(1, cartridge.Header.ChrRomBanks);
        Assert.Equal(16 * 1024, cartridge.PrgRom.Length);
        Assert.Equal(8 * 1024, cartridge.ChrMemory.Length);
    }

    [Fact]
    public void MirrorsSinglePrgBankIntoUpperCpuRomRange()
    {
        var rom = CreateRom(prgBanks: 1, chrBanks: 1);
        rom[16] = 0x42;

        var cartridge = INesRomLoader.Load(rom);

        Assert.Equal(0x42, cartridge.CpuRead(0x8000));
        Assert.Equal(0x42, cartridge.CpuRead(0xC000));
    }

    [Fact]
    public void MapsTwoPrgBanksWithoutMirroring()
    {
        var rom = CreateRom(prgBanks: 2, chrBanks: 1);
        rom[16] = 0x11;
        rom[16 + 16 * 1024] = 0x22;

        var cartridge = INesRomLoader.Load(rom);

        Assert.Equal(0x11, cartridge.CpuRead(0x8000));
        Assert.Equal(0x22, cartridge.CpuRead(0xC000));
    }

    [Fact]
    public void AllocatesWritableChrRamWhenHeaderHasNoChrRom()
    {
        var rom = CreateRom(prgBanks: 1, chrBanks: 0);
        var cartridge = INesRomLoader.Load(rom);

        cartridge.PpuWrite(0x0010, 0x7A);

        Assert.True(cartridge.Header.UsesChrRam);
        Assert.Equal(8 * 1024, cartridge.ChrMemory.Length);
        Assert.Equal(0x7A, cartridge.PpuRead(0x0010));
    }

    [Fact]
    public void IgnoresPpuWritesToChrRom()
    {
        var rom = CreateRom(prgBanks: 1, chrBanks: 1);
        var chrOffset = 16 + 16 * 1024;
        rom[chrOffset + 0x20] = 0x55;
        var cartridge = INesRomLoader.Load(rom);

        cartridge.PpuWrite(0x0020, 0xAA);

        Assert.Equal(0x55, cartridge.PpuRead(0x0020));
    }

    [Fact]
    public void SkipsTrainerBeforePrgData()
    {
        var rom = CreateRom(prgBanks: 1, chrBanks: 1, hasTrainer: true);
        var prgOffset = 16 + 512;
        rom[prgOffset] = 0x99;

        var cartridge = INesRomLoader.Load(rom);

        Assert.True(cartridge.Header.HasTrainer);
        Assert.Equal(0x99, cartridge.CpuRead(0x8000));
    }

    [Fact]
    public void SupportsWritablePrgRamAtSixThousand()
    {
        var rom = CreateRom(prgBanks: 1, chrBanks: 1);
        var cartridge = INesRomLoader.Load(rom);

        cartridge.CpuWrite(0x6000, 0xDE);
        cartridge.CpuWrite(0x6001, 0xB0);
        cartridge.CpuWrite(0x6002, 0x61);

        Assert.Equal(0xDE, cartridge.CpuRead(0x6000));
        Assert.Equal(0xB0, cartridge.CpuRead(0x6001));
        Assert.Equal(0x61, cartridge.CpuRead(0x6002));
    }

    [Fact]
    public void ExposesBatteryBackedSaveRamWhenHeaderBatteryBitIsSet()
    {
        var rom = CreateRom(prgBanks: 1, chrBanks: 1, hasBattery: true);
        var cartridge = INesRomLoader.Load(rom);

        cartridge.CpuWrite(0x6000, 0xC5);

        Assert.True(cartridge.HasBatteryBackedSaveRam);
        Assert.Equal(8 * 1024, cartridge.SaveRam.Length);
        Assert.Equal(0xC5, cartridge.SaveRam[0]);
    }

    [Fact]
    public void DoesNotTreatPlainPrgRamAsPersistentSaveRam()
    {
        var rom = CreateRom(prgBanks: 1, chrBanks: 1);
        var cartridge = INesRomLoader.Load(rom);

        Assert.False(cartridge.HasBatteryBackedSaveRam);
        Assert.Equal(8 * 1024, cartridge.SaveRam.Length);
    }

    [Fact]
    public void LoadsSaveRamIntoMapperPrgRam()
    {
        var rom = CreateRom(prgBanks: 2, chrBanks: 0, mapper: 2, hasBattery: true);
        var cartridge = INesRomLoader.Load(rom);
        var saveRam = new byte[8 * 1024];
        saveRam[0] = 0xA1;
        saveRam[^1] = 0x1A;

        cartridge.LoadSaveRam(saveRam);

        Assert.Equal(0xA1, cartridge.CpuRead(0x6000));
        Assert.Equal(0x1A, cartridge.CpuRead(0x7FFF));
    }

    [Fact]
    public void RejectsUnsupportedMapper()
    {
        var rom = CreateRom(prgBanks: 1, chrBanks: 1, mapper: 5);

        var ex = Assert.Throws<NotSupportedException>(() => INesRomLoader.Load(rom));

        Assert.Contains("Mapper 5", ex.Message);
    }

    [Fact]
    public void Mapper4StartsWithLastPrgBanksFixed()
    {
        var rom = CreateRom(prgBanks: 4, chrBanks: 1, mapper: 4);
        SetPrg8KBankMarker(rom, bank: 0, 0x10);
        SetPrg8KBankMarker(rom, bank: 6, 0x16);
        SetPrg8KBankMarker(rom, bank: 7, 0x17);

        var cartridge = INesRomLoader.Load(rom);

        Assert.Equal(4, cartridge.Header.MapperNumber);
        Assert.Equal(0x10, cartridge.CpuRead(0x8000));
        Assert.Equal(0x16, cartridge.CpuRead(0xC000));
        Assert.Equal(0x17, cartridge.CpuRead(0xE000));
    }

    [Fact]
    public void Mapper4SupportsSinglePrgRomBank()
    {
        var rom = CreateRom(prgBanks: 1, chrBanks: 1, mapper: 4);
        SetPrg8KBankMarker(rom, bank: 0, 0x10);
        SetPrg8KBankMarker(rom, bank: 1, 0x11);

        var cartridge = INesRomLoader.Load(rom);

        Assert.Equal(0x10, cartridge.CpuRead(0x8000));
        Assert.Equal(0x10, cartridge.CpuRead(0xC000));
        Assert.Equal(0x11, cartridge.CpuRead(0xE000));
    }

    [Fact]
    public void Mapper4SwitchesPrgBanks()
    {
        var rom = CreateRom(prgBanks: 4, chrBanks: 1, mapper: 4);
        SetPrg8KBankMarker(rom, bank: 2, 0x12);
        SetPrg8KBankMarker(rom, bank: 3, 0x13);
        SetPrg8KBankMarker(rom, bank: 6, 0x16);
        SetPrg8KBankMarker(rom, bank: 7, 0x17);
        var cartridge = INesRomLoader.Load(rom);

        cartridge.CpuWrite(0x8000, 0x06);
        cartridge.CpuWrite(0x8001, 0x02);
        cartridge.CpuWrite(0x8000, 0x07);
        cartridge.CpuWrite(0x8001, 0x03);

        Assert.Equal(0x12, cartridge.CpuRead(0x8000));
        Assert.Equal(0x13, cartridge.CpuRead(0xA000));
        Assert.Equal(0x16, cartridge.CpuRead(0xC000));
        Assert.Equal(0x17, cartridge.CpuRead(0xE000));
    }

    [Fact]
    public void Mapper4SupportsInvertedPrgBankMode()
    {
        var rom = CreateRom(prgBanks: 4, chrBanks: 1, mapper: 4);
        SetPrg8KBankMarker(rom, bank: 2, 0x12);
        SetPrg8KBankMarker(rom, bank: 6, 0x16);
        var cartridge = INesRomLoader.Load(rom);

        cartridge.CpuWrite(0x8000, 0x46);
        cartridge.CpuWrite(0x8001, 0x02);

        Assert.Equal(0x16, cartridge.CpuRead(0x8000));
        Assert.Equal(0x12, cartridge.CpuRead(0xC000));
    }

    [Fact]
    public void Mapper4SwitchesChrBanks()
    {
        var rom = CreateRom(prgBanks: 2, chrBanks: 2, mapper: 4);
        SetChr1KBankMarker(rom, prgBanks: 2, bank: 4, 0x24);
        SetChr1KBankMarker(rom, prgBanks: 2, bank: 5, 0x25);
        SetChr1KBankMarker(rom, prgBanks: 2, bank: 6, 0x26);
        SetChr1KBankMarker(rom, prgBanks: 2, bank: 7, 0x27);
        SetChr1KBankMarker(rom, prgBanks: 2, bank: 8, 0x28);
        var cartridge = INesRomLoader.Load(rom);

        cartridge.CpuWrite(0x8000, 0x00);
        cartridge.CpuWrite(0x8001, 0x04);
        cartridge.CpuWrite(0x8000, 0x01);
        cartridge.CpuWrite(0x8001, 0x06);
        cartridge.CpuWrite(0x8000, 0x02);
        cartridge.CpuWrite(0x8001, 0x08);

        Assert.Equal(0x24, cartridge.PpuRead(0x0000));
        Assert.Equal(0x25, cartridge.PpuRead(0x0400));
        Assert.Equal(0x26, cartridge.PpuRead(0x0800));
        Assert.Equal(0x27, cartridge.PpuRead(0x0C00));
        Assert.Equal(0x28, cartridge.PpuRead(0x1000));
    }

    [Theory]
    [InlineData(0, MirroringMode.Vertical)]
    [InlineData(1, MirroringMode.Horizontal)]
    public void Mapper4SwitchesMirroring(byte value, MirroringMode expected)
    {
        var rom = CreateRom(prgBanks: 2, chrBanks: 1, mapper: 4);
        var cartridge = INesRomLoader.Load(rom);

        cartridge.CpuWrite(0xA000, value);

        Assert.Equal(expected, cartridge.CurrentMirroringMode);
    }

    [Fact]
    public void Mapper4IrqCounterRaisesAndAcknowledgesIrq()
    {
        var rom = CreateRom(prgBanks: 2, chrBanks: 1, mapper: 4);
        var cartridge = INesRomLoader.Load(rom);

        cartridge.CpuWrite(0xC000, 0x02);
        cartridge.CpuWrite(0xC001, 0x00);
        cartridge.CpuWrite(0xE001, 0x00);
        ClockMapper4A12(cartridge);
        ClockMapper4A12(cartridge);

        Assert.False(cartridge.IsIrqPending);

        ClockMapper4A12(cartridge);

        Assert.True(cartridge.IsIrqPending);

        cartridge.CpuWrite(0xE000, 0x00);

        Assert.False(cartridge.IsIrqPending);
    }

    [Fact]
    public void Mapper2SwitchesPrgBankAtEightThousandAndFixesLastBank()
    {
        var rom = CreateRom(prgBanks: 4, chrBanks: 0, mapper: 2);
        SetPrgBankMarker(rom, bank: 2, 0x12);
        SetPrgBankMarker(rom, bank: 3, 0x13);
        var cartridge = INesRomLoader.Load(rom);

        cartridge.CpuWrite(0x8000, 0x02);

        Assert.Equal(2, cartridge.Header.MapperNumber);
        Assert.Equal(0x12, cartridge.CpuRead(0x8000));
        Assert.Equal(0x13, cartridge.CpuRead(0xC000));
    }

    [Fact]
    public void Mapper2WrapsPrgBankSelection()
    {
        var rom = CreateRom(prgBanks: 4, chrBanks: 0, mapper: 2);
        SetPrgBankMarker(rom, bank: 1, 0x11);
        var cartridge = INesRomLoader.Load(rom);

        cartridge.CpuWrite(0xFFFF, 0x05);

        Assert.Equal(0x11, cartridge.CpuRead(0x8000));
    }

    [Fact]
    public void Mapper2ProvidesWritableChrRam()
    {
        var rom = CreateRom(prgBanks: 2, chrBanks: 0, mapper: 2);
        var cartridge = INesRomLoader.Load(rom);

        cartridge.PpuWrite(0x0140, 0x62);

        Assert.True(cartridge.Header.UsesChrRam);
        Assert.Equal(0x62, cartridge.PpuRead(0x0140));
    }

    [Fact]
    public void Mapper2UsesFixedHeaderMirroring()
    {
        var rom = CreateRom(prgBanks: 2, chrBanks: 0, mapper: 2, verticalMirroring: true);
        var cartridge = INesRomLoader.Load(rom);

        cartridge.CpuWrite(0x8000, 0x01);

        Assert.Equal(MirroringMode.Vertical, cartridge.CurrentMirroringMode);
    }

    [Fact]
    public void Mapper2SupportsWritablePrgRamWhenPresent()
    {
        var rom = CreateRom(prgBanks: 2, chrBanks: 0, mapper: 2);
        var cartridge = INesRomLoader.Load(rom);

        cartridge.CpuWrite(0x6000, 0xC0);
        cartridge.CpuWrite(0x7FFF, 0xDE);

        Assert.Equal(0xC0, cartridge.CpuRead(0x6000));
        Assert.Equal(0xDE, cartridge.CpuRead(0x7FFF));
    }

    [Fact]
    public void Mapper3SwitchesChrBanks()
    {
        var rom = CreateRom(prgBanks: 2, chrBanks: 2, mapper: 3);
        var chrOffset = 16 + 2 * 16 * 1024;
        rom[chrOffset] = 0x11;
        rom[chrOffset + 8 * 1024] = 0x22;

        var cartridge = INesRomLoader.Load(rom);

        Assert.Equal(3, cartridge.Header.MapperNumber);
        Assert.Equal(0x11, cartridge.PpuRead(0x0000));

        cartridge.CpuWrite(0x8000, 0x01);

        Assert.Equal(0x22, cartridge.PpuRead(0x0000));
    }

    [Fact]
    public void Mapper7SwitchesThirtyTwoKilobytePrgBanks()
    {
        var rom = CreateRom(prgBanks: 8, chrBanks: 0, mapper: 7);
        SetPrgBankMarker(rom, bank: 4, 0x24);
        SetPrgBankMarker(rom, bank: 5, 0x25);
        var cartridge = INesRomLoader.Load(rom);

        cartridge.CpuWrite(0x8000, 0x02);

        Assert.Equal(7, cartridge.Header.MapperNumber);
        Assert.Equal(0x24, cartridge.CpuRead(0x8000));
        Assert.Equal(0x25, cartridge.CpuRead(0xC000));
    }

    [Fact]
    public void Mapper7WrapsPrgBankSelection()
    {
        var rom = CreateRom(prgBanks: 4, chrBanks: 0, mapper: 7);
        SetPrgBankMarker(rom, bank: 2, 0x22);
        SetPrgBankMarker(rom, bank: 3, 0x23);
        var cartridge = INesRomLoader.Load(rom);

        cartridge.CpuWrite(0xFFFF, 0x05);

        Assert.Equal(0x22, cartridge.CpuRead(0x8000));
        Assert.Equal(0x23, cartridge.CpuRead(0xC000));
    }

    [Theory]
    [InlineData(0x00, MirroringMode.OneScreenLower)]
    [InlineData(0x10, MirroringMode.OneScreenUpper)]
    public void Mapper7SwitchesOneScreenMirroring(byte value, MirroringMode expected)
    {
        var rom = CreateRom(prgBanks: 2, chrBanks: 0, mapper: 7);
        var cartridge = INesRomLoader.Load(rom);

        cartridge.CpuWrite(0x8000, value);

        Assert.Equal(expected, cartridge.CurrentMirroringMode);
    }

    [Fact]
    public void Mapper7ProvidesWritableChrRam()
    {
        var rom = CreateRom(prgBanks: 2, chrBanks: 0, mapper: 7);
        var cartridge = INesRomLoader.Load(rom);

        cartridge.PpuWrite(0x01A0, 0x77);

        Assert.True(cartridge.Header.UsesChrRam);
        Assert.Equal(0x77, cartridge.PpuRead(0x01A0));
    }

    [Fact]
    public void Mapper1StartsWithLastPrgBankFixedAtC000()
    {
        var rom = CreateRom(prgBanks: 4, chrBanks: 1, mapper: 1);
        SetPrgBankMarker(rom, bank: 0, 0x10);
        SetPrgBankMarker(rom, bank: 3, 0x13);

        var cartridge = INesRomLoader.Load(rom);

        Assert.Equal(1, cartridge.Header.MapperNumber);
        Assert.Equal(0x10, cartridge.CpuRead(0x8000));
        Assert.Equal(0x13, cartridge.CpuRead(0xC000));
    }

    [Fact]
    public void Mapper1SwitchesPrgBankAtEightThousandInModeThree()
    {
        var rom = CreateRom(prgBanks: 4, chrBanks: 1, mapper: 1);
        SetPrgBankMarker(rom, bank: 2, 0x12);
        SetPrgBankMarker(rom, bank: 3, 0x13);
        var cartridge = INesRomLoader.Load(rom);

        WriteMapper1Register(cartridge, 0xE000, 0x02);

        Assert.Equal(0x12, cartridge.CpuRead(0x8000));
        Assert.Equal(0x13, cartridge.CpuRead(0xC000));
    }

    [Fact]
    public void Mapper1SupportsFixedFirstPrgBankMode()
    {
        var rom = CreateRom(prgBanks: 4, chrBanks: 1, mapper: 1);
        SetPrgBankMarker(rom, bank: 0, 0x10);
        SetPrgBankMarker(rom, bank: 2, 0x12);
        var cartridge = INesRomLoader.Load(rom);

        WriteMapper1Register(cartridge, 0x8000, 0x08);
        WriteMapper1Register(cartridge, 0xE000, 0x02);

        Assert.Equal(0x10, cartridge.CpuRead(0x8000));
        Assert.Equal(0x12, cartridge.CpuRead(0xC000));
    }

    [Fact]
    public void Mapper1SupportsThirtyTwoKilobytePrgBankMode()
    {
        var rom = CreateRom(prgBanks: 4, chrBanks: 1, mapper: 1);
        SetPrgBankMarker(rom, bank: 2, 0x12);
        SetPrgBankMarker(rom, bank: 3, 0x13);
        var cartridge = INesRomLoader.Load(rom);

        WriteMapper1Register(cartridge, 0x8000, 0x00);
        WriteMapper1Register(cartridge, 0xE000, 0x03);

        Assert.Equal(0x12, cartridge.CpuRead(0x8000));
        Assert.Equal(0x13, cartridge.CpuRead(0xC000));
    }

    [Fact]
    public void Mapper1SwitchesSeparateFourKilobyteChrBanks()
    {
        var rom = CreateRom(prgBanks: 2, chrBanks: 2, mapper: 1);
        SetChrBankMarker(rom, prgBanks: 2, bank: 2, 0x22);
        SetChrBankMarker(rom, prgBanks: 2, bank: 3, 0x23);
        var cartridge = INesRomLoader.Load(rom);

        WriteMapper1Register(cartridge, 0x8000, 0x10);
        WriteMapper1Register(cartridge, 0xA000, 0x02);
        WriteMapper1Register(cartridge, 0xC000, 0x03);

        Assert.Equal(0x22, cartridge.PpuRead(0x0000));
        Assert.Equal(0x23, cartridge.PpuRead(0x1000));
    }

    [Fact]
    public void Mapper1CanDisablePrgRam()
    {
        var rom = CreateRom(prgBanks: 2, chrBanks: 1, mapper: 1);
        var cartridge = INesRomLoader.Load(rom);
        cartridge.CpuWrite(0x6000, 0x5A);

        WriteMapper1Register(cartridge, 0xE000, 0x10);
        cartridge.CpuWrite(0x6000, 0xA5);

        Assert.Equal(0, cartridge.CpuRead(0x6000));

        WriteMapper1Register(cartridge, 0xE000, 0x00);

        Assert.Equal(0x5A, cartridge.CpuRead(0x6000));
    }

    [Theory]
    [InlineData(0, MirroringMode.OneScreenLower)]
    [InlineData(1, MirroringMode.OneScreenUpper)]
    [InlineData(2, MirroringMode.Vertical)]
    [InlineData(3, MirroringMode.Horizontal)]
    public void Mapper1SwitchesMirroring(int controlValue, MirroringMode expected)
    {
        var rom = CreateRom(prgBanks: 2, chrBanks: 1, mapper: 1);
        var cartridge = INesRomLoader.Load(rom);

        WriteMapper1Register(cartridge, 0x8000, (byte)controlValue);

        Assert.Equal(expected, cartridge.CurrentMirroringMode);
    }

    [Fact]
    public void RejectsInvalidMagic()
    {
        var rom = CreateRom(prgBanks: 1, chrBanks: 1);
        rom[0] = 0;

        Assert.Throws<InvalidRomException>(() => INesRomLoader.Load(rom));
    }

    private static byte[] CreateRom(
        byte prgBanks,
        byte chrBanks,
        bool hasTrainer = false,
        byte mapper = 0,
        bool verticalMirroring = false,
        bool hasBattery = false)
    {
        var trainerSize = hasTrainer ? 512 : 0;
        var rom = new byte[16 + trainerSize + prgBanks * 16 * 1024 + chrBanks * 8 * 1024];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = prgBanks;
        rom[5] = chrBanks;
        rom[6] = (byte)(
            (mapper << 4) |
            (hasTrainer ? 0b0000_0100 : 0) |
            (hasBattery ? 0b0000_0010 : 0) |
            (verticalMirroring ? 0b0000_0001 : 0));
        return rom;
    }

    private static void SetPrgBankMarker(byte[] rom, int bank, byte value)
    {
        rom[16 + bank * 16 * 1024] = value;
    }

    private static void SetPrg8KBankMarker(byte[] rom, int bank, byte value)
    {
        rom[16 + bank * 8 * 1024] = value;
    }

    private static void SetChrBankMarker(byte[] rom, int prgBanks, int bank, byte value)
    {
        rom[16 + prgBanks * 16 * 1024 + bank * 4 * 1024] = value;
    }

    private static void SetChr1KBankMarker(byte[] rom, int prgBanks, int bank, byte value)
    {
        rom[16 + prgBanks * 16 * 1024 + bank * 1024] = value;
    }

    private static void ClockMapper4A12(Cartridge cartridge)
    {
        cartridge.NotifyPpuAddress(0x0000);
        cartridge.NotifyPpuAddress(0x1000);
    }

    private static void WriteMapper1Register(Cartridge cartridge, ushort address, byte value)
    {
        for (var i = 0; i < 5; i++)
        {
            cartridge.CpuWrite(address, (byte)((value >> i) & 0x01));
        }
    }

    private static string TestRomPath(params string[] parts)
    {
        var root = FindWorkspaceRoot(AppContext.BaseDirectory);
        return Path.Combine(new[] { root, "test-roms", "nes-test-roms" }.Concat(parts).ToArray());
    }

    private static string FindWorkspaceRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "test-roms", "nes-test-roms")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find workspace root containing test-roms/nes-test-roms.");
    }
}
