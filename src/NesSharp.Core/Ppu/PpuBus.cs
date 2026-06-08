using NesSharp.Core.Cartridge;

namespace NesSharp.Core.Ppu;

public sealed class PpuBus
{
    private readonly Cartridge.Cartridge cartridge;
    private readonly byte[] registers = new byte[8];

    public PpuBus(Cartridge.Cartridge cartridge)
    {
        this.cartridge = cartridge;
    }

    public byte ReadRegister(ushort address)
    {
        return registers[address & 0x0007];
    }

    public void WriteRegister(ushort address, byte value)
    {
        registers[address & 0x0007] = value;
    }

    public byte ReadPatternTable(ushort address) => cartridge.PpuRead(address);

    public void WritePatternTable(ushort address, byte value) => cartridge.PpuWrite(address, value);
}

