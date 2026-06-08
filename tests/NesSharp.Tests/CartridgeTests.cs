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
    public void RejectsUnsupportedMapper()
    {
        var rom = CreateRom(prgBanks: 1, chrBanks: 1);
        rom[6] = 0x10;

        var ex = Assert.Throws<NotSupportedException>(() => INesRomLoader.Load(rom));

        Assert.Contains("Mapper 1", ex.Message);
    }

    [Fact]
    public void RejectsInvalidMagic()
    {
        var rom = CreateRom(prgBanks: 1, chrBanks: 1);
        rom[0] = 0;

        Assert.Throws<InvalidRomException>(() => INesRomLoader.Load(rom));
    }

    private static byte[] CreateRom(byte prgBanks, byte chrBanks, bool hasTrainer = false)
    {
        var trainerSize = hasTrainer ? 512 : 0;
        var rom = new byte[16 + trainerSize + prgBanks * 16 * 1024 + chrBanks * 8 * 1024];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = prgBanks;
        rom[5] = chrBanks;
        rom[6] = hasTrainer ? (byte)0b0000_0100 : (byte)0;
        return rom;
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

