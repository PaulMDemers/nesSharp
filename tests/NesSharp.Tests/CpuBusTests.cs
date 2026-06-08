using NesSharp.Core.Cartridge;
using NesSharp.Core.Cpu;
using NesSharp.Core.Memory;

namespace NesSharp.Tests;

public sealed class CpuBusTests
{
    [Fact]
    public void MirrorsInternalRamEveryTwoKilobytes()
    {
        var bus = new CpuBus(CreateCartridgeWithResetVector());

        bus.Write(0x0002, 0x34);

        Assert.Equal(0x34, bus.Read(0x0002));
        Assert.Equal(0x34, bus.Read(0x0802));
        Assert.Equal(0x34, bus.Read(0x1002));
        Assert.Equal(0x34, bus.Read(0x1802));
    }

    [Fact]
    public void MirrorsPpuRegistersThroughThreeFff()
    {
        var bus = new CpuBus(CreateCartridgeWithResetVector());

        bus.Write(0x2000, 0x80);

        Assert.Equal(0x80, bus.Read(0x2000));
        Assert.Equal(0x80, bus.Read(0x2008));
        Assert.Equal(0x80, bus.Read(0x3FF8));
    }

    [Fact]
    public void CpuResetReadsResetVectorFromCartridge()
    {
        var bus = new CpuBus(CreateCartridgeWithResetVector(0xC123));
        var cpu = new Cpu6502(bus);

        cpu.Reset();

        Assert.Equal(0xC123, cpu.ProgramCounter);
        Assert.Equal(0xFD, cpu.StackPointer);
        Assert.Equal((byte)(ProcessorStatus.Unused | ProcessorStatus.InterruptDisable), cpu.Status);
        Assert.Equal(7UL, cpu.Cycles);
    }

    [Fact]
    public void CpuNmiPushesStateAndJumpsToNmiVector()
    {
        var bus = new CpuBus(CreateCartridgeWithResetAndNmiVectors(resetVector: 0x8000, nmiVector: 0x9000));
        var cpu = new Cpu6502(bus);
        cpu.Reset();
        cpu.SetProgramCounter(0x8123);
        cpu.SetCycles(100);

        cpu.RequestNmi();
        var cycles = cpu.Step();

        Assert.Equal(7, cycles);
        Assert.Equal(0x9000, cpu.ProgramCounter);
        Assert.Equal(0xFA, cpu.StackPointer);
        Assert.Equal((byte)0x81, bus.Read(0x01FD));
        Assert.Equal((byte)0x23, bus.Read(0x01FC));
        Assert.Equal((byte)(ProcessorStatus.Unused | ProcessorStatus.InterruptDisable), bus.Read(0x01FB));
        Assert.Equal((byte)(ProcessorStatus.Unused | ProcessorStatus.InterruptDisable), cpu.Status);
        Assert.Equal(107UL, cpu.Cycles);
    }

    private static Cartridge CreateCartridgeWithResetVector(ushort resetVector = 0x8000)
    {
        return CreateCartridgeWithResetAndNmiVectors(resetVector, 0x8000);
    }

    private static Cartridge CreateCartridgeWithResetAndNmiVectors(ushort resetVector, ushort nmiVector)
    {
        var rom = new byte[16 + 16 * 1024 + 8 * 1024];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = 1;
        rom[5] = 1;

        var nmiVectorOffset = 16 + 0x3FFA;
        rom[nmiVectorOffset] = (byte)(nmiVector & 0x00FF);
        rom[nmiVectorOffset + 1] = (byte)(nmiVector >> 8);

        var resetVectorOffset = 16 + 0x3FFC;
        rom[resetVectorOffset] = (byte)(resetVector & 0x00FF);
        rom[resetVectorOffset + 1] = (byte)(resetVector >> 8);

        return INesRomLoader.Load(rom);
    }
}
