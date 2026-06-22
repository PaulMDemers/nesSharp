using NesSharp.Core.Cartridge;
using NesSharp.Core.Cpu;
using NesSharp.Core.Memory;

namespace NesSharp.Tests;

public sealed class CpuBusTests
{
    [Fact]
    public void InternalRamPowersOnWithAlternatingMamePattern()
    {
        var bus = new CpuBus(CreateCartridgeWithResetVector());

        Assert.Equal(0x00, bus.Read(0x0000));
        Assert.Equal(0xFF, bus.Read(0x0001));
        Assert.Equal(0x00, bus.Read(0x0002));
        Assert.Equal(0xFF, bus.Read(0x0003));
        Assert.Equal(0x00, bus.Read(0x0800));
        Assert.Equal(0xFF, bus.Read(0x0801));
    }

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
        var bus = new CpuBus(CreateCartridgeWithVectors(resetVector: 0x8000, nmiVector: 0x9000));
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

    [Fact]
    public void CpuIrqIsIgnoredWhileInterruptDisableFlagIsSet()
    {
        var bus = new CpuBus(CreateCartridgeWithVectors(resetVector: 0x8000, irqVector: 0x9000));
        var cpu = new Cpu6502(bus);
        cpu.Reset();

        cpu.RequestIrq();
        var cycles = cpu.Step();

        Assert.Equal(2, cycles);
        Assert.Equal(0x8001, cpu.ProgramCounter);
        Assert.Equal((byte)(ProcessorStatus.Unused | ProcessorStatus.InterruptDisable), cpu.Status);
    }

    [Fact]
    public void CpuIrqPushesStateAndJumpsToIrqVectorWhenInterruptsAreEnabled()
    {
        var bus = new CpuBus(CreateCartridgeWithVectors(
            resetVector: 0x8000,
            irqVector: 0x9000,
            firstOpcode: 0x58,
            secondOpcode: 0xEA));
        var cpu = new Cpu6502(bus);
        cpu.Reset();
        cpu.Step();
        cpu.Step();
        cpu.SetProgramCounter(0x8123);
        cpu.SetCycles(100);

        cpu.RequestIrq();
        var cycles = cpu.Step();

        Assert.Equal(7, cycles);
        Assert.Equal(0x9000, cpu.ProgramCounter);
        Assert.Equal(0xFA, cpu.StackPointer);
        Assert.Equal((byte)0x81, bus.Read(0x01FD));
        Assert.Equal((byte)0x23, bus.Read(0x01FC));
        Assert.Equal((byte)ProcessorStatus.Unused, bus.Read(0x01FB));
        Assert.Equal((byte)(ProcessorStatus.Unused | ProcessorStatus.InterruptDisable), cpu.Status);
        Assert.Equal(107UL, cpu.Cycles);
    }

    [Fact]
    public void CpuCliDelaysPendingIrqForOneInstruction()
    {
        var bus = new CpuBus(CreateCartridgeWithVectors(
            resetVector: 0x8000,
            irqVector: 0x9000,
            firstOpcode: 0x58,
            secondOpcode: 0xEA));
        var cpu = new Cpu6502(bus);
        cpu.Reset();

        cpu.RequestIrq();

        Assert.Equal(2, cpu.Step());
        Assert.Equal(0x8001, cpu.ProgramCounter);
        Assert.Equal((byte)ProcessorStatus.Unused, cpu.Status);

        Assert.Equal(2, cpu.Step());
        Assert.Equal(0x8002, cpu.ProgramCounter);

        Assert.Equal(7, cpu.Step());
        Assert.Equal(0x9000, cpu.ProgramCounter);
    }

    [Fact]
    public void CpuIrqRequestedDuringInstructionIsDelayedForOneInstruction()
    {
        var bus = new CpuBus(CreateCartridgeWithVectors(
            resetVector: 0x8000,
            irqVector: 0x9000,
            firstOpcode: 0x58,
            secondOpcode: 0xEA,
            thirdOpcode: 0xA9,
            fourthOpcode: 0x01,
            fifthOpcode: 0x18,
            sixthOpcode: 0xEA));
        var cpu = new Cpu6502(bus);
        cpu.Reset();
        cpu.Step();
        cpu.Step();

        bus.SetCpuCycleCallback(() =>
        {
            if (bus.CpuAccessCycles == 2)
            {
                cpu.RequestIrq();
            }
        });
        cpu.Step();

        Assert.Equal(0x8004, cpu.ProgramCounter);
        Assert.Equal((byte)0x01, cpu.A);

        cpu.Step();

        Assert.Equal(0x8005, cpu.ProgramCounter);

        cpu.Step();

        Assert.Equal(0x9000, cpu.ProgramCounter);
    }

    [Fact]
    public void CpuNmiTakesPriorityOverPendingIrq()
    {
        var bus = new CpuBus(CreateCartridgeWithVectors(resetVector: 0x8000, nmiVector: 0x9000, irqVector: 0xA000));
        var cpu = new Cpu6502(bus);
        cpu.Reset();
        cpu.SetProgramCounter(0x8123);

        cpu.RequestIrq();
        cpu.RequestNmi();
        cpu.Step();

        Assert.Equal(0x9000, cpu.ProgramCounter);
    }

    [Fact]
    public void NmiCanHijackBrkVectorAfterBreakStatusIsPushed()
    {
        var bus = new CpuBus(CreateCartridgeWithVectors(
            resetVector: 0x8000,
            nmiVector: 0x9000,
            irqVector: 0xA000,
            firstOpcode: 0x00));
        var cpu = new Cpu6502(bus);
        var requestedNmi = false;
        bus.SetCpuCycleCallback(() =>
        {
            if (!requestedNmi && bus.CpuAccessCycles == 5)
            {
                requestedNmi = true;
                cpu.RequestNmi();
            }
        });
        cpu.Reset();

        var cycles = cpu.Step();

        Assert.Equal(7, cycles);
        Assert.Equal(0x9000, cpu.ProgramCounter);
        Assert.Equal((byte)ProcessorStatus.Break, (byte)(bus.Read(0x01FB) & (byte)ProcessorStatus.Break));
    }

    [Fact]
    public void NmiCanHijackIrqVectorAfterStatusIsPushed()
    {
        var bus = new CpuBus(CreateCartridgeWithVectors(
            resetVector: 0x8000,
            nmiVector: 0x9000,
            irqVector: 0xA000,
            firstOpcode: 0x58,
            secondOpcode: 0xEA));
        var cpu = new Cpu6502(bus);
        var requestedNmi = false;
        bus.SetCpuCycleCallback(() =>
        {
            if (!requestedNmi && bus.CpuAccessCycles == 5)
            {
                requestedNmi = true;
                cpu.RequestNmi();
            }
        });
        cpu.Reset();
        cpu.Step();
        cpu.Step();
        cpu.RequestIrq();

        var cycles = cpu.Step();

        Assert.Equal(7, cycles);
        Assert.Equal(0x9000, cpu.ProgramCounter);
        Assert.Equal(0xFA, cpu.StackPointer);
        Assert.Equal(0, bus.Read(0x01FB) & (byte)ProcessorStatus.Break);
    }

    [Fact]
    public void OamDmaAddsFiveHundredFourteenCyclesWhenAligned()
    {
        var bus = new CpuBus(CreateCartridgeWithResetVector());

        bus.BeginCpuInstruction();
        bus.Write(0x4014, 0x02);
        bus.EndCpuInstruction();

        Assert.Equal(515, bus.CpuAccessCycles);
        Assert.Equal(1, bus.InstructionAccessCycles);
    }

    [Fact]
    public void OamDmaAddsAlignmentCycleWhenStartingOnGetCycle()
    {
        var bus = new CpuBus(CreateCartridgeWithResetVector());
        bus.BeginCpuInstruction();
        bus.Read(0x8000);
        bus.EndCpuInstruction();

        bus.BeginCpuInstruction();
        bus.Write(0x4014, 0x02);
        bus.EndCpuInstruction();

        Assert.Equal(516, bus.CpuAccessCycles);
        Assert.Equal(1, bus.InstructionAccessCycles);
    }

    [Fact]
    public void SyntheticInstructionAccessDoesNotAdvanceDmaPhase()
    {
        var bus = new CpuBus(CreateCartridgeWithResetVector());

        bus.BeginCpuInstruction();
        bus.ClockSyntheticInstructionAccess();
        bus.Write(0x4014, 0x02);
        bus.EndCpuInstruction();

        Assert.Equal(516, bus.CpuAccessCycles);
        Assert.Equal(2, bus.InstructionAccessCycles);
    }

    [Fact]
    public void NonBusCpuCycleAdvancesDmaPhaseWithoutCountingMemoryAccess()
    {
        var bus = new CpuBus(CreateCartridgeWithResetVector());

        bus.BeginCpuInstruction();
        bus.AdvanceDmaPhase();
        bus.Write(0x4014, 0x02);
        bus.EndCpuInstruction();

        Assert.Equal(516, bus.CpuAccessCycles);
        Assert.Equal(1, bus.InstructionAccessCycles);
    }

    [Fact]
    public void OamDmaServicesPendingDmcDmaDuringTransfer()
    {
        var bus = new CpuBus(CreateCartridgeWithResetVector());
        bus.SetCpuCycleCallback(bus.ApuBus.Clock);
        bus.WriteRaw(0x4013, 0x00);
        bus.WriteRaw(0x4015, 0x10);

        Assert.True(bus.ApuBus.IsDmcDmaPending);

        bus.BeginCpuInstruction();
        bus.Write(0x4014, 0x02);
        bus.EndCpuInstruction();

        Assert.Equal(517, bus.CpuAccessCycles);
        Assert.Equal(1, bus.InstructionAccessCycles);
        Assert.False(bus.ApuBus.IsDmcDmaPending);
        Assert.Equal(0, bus.ApuBus.Dmc.BytesRemaining);
    }

    [Fact]
    public void DmcDmaDuringPpuDataReadRepeatsSideEffectReads()
    {
        var bus = new CpuBus(CreateCartridgeWithResetVector());
        bus.Write(0x2006, 0x20);
        bus.Write(0x2006, 0x00);
        bus.Write(0x2007, 0x11);
        bus.Write(0x2007, 0x22);
        bus.Write(0x2007, 0x33);
        bus.Write(0x2006, 0x20);
        bus.Write(0x2006, 0x00);
        bus.WriteRaw(0x4013, 0x00);
        bus.WriteRaw(0x4015, 0x10);
        bus.ApuBus.Clock();
        bus.ApuBus.Clock();

        Assert.Equal(0x00, bus.Read(0x2007));

        Assert.Equal(0x33, bus.Read(0x2007));
    }

    private static Cartridge CreateCartridgeWithResetVector(ushort resetVector = 0x8000)
    {
        return CreateCartridgeWithVectors(resetVector);
    }

    private static Cartridge CreateCartridgeWithVectors(
        ushort resetVector,
        ushort nmiVector = 0x8000,
        ushort irqVector = 0x8000,
        byte firstOpcode = 0xEA,
        byte secondOpcode = 0xEA,
        byte thirdOpcode = 0xEA,
        byte fourthOpcode = 0xEA,
        byte fifthOpcode = 0xEA,
        byte sixthOpcode = 0xEA)
    {
        var rom = new byte[16 + 16 * 1024 + 8 * 1024];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = 1;
        rom[5] = 1;
        rom[16] = firstOpcode;
        rom[17] = secondOpcode;
        rom[18] = thirdOpcode;
        rom[19] = fourthOpcode;
        rom[20] = fifthOpcode;
        rom[21] = sixthOpcode;

        var nmiVectorOffset = 16 + 0x3FFA;
        rom[nmiVectorOffset] = (byte)(nmiVector & 0x00FF);
        rom[nmiVectorOffset + 1] = (byte)(nmiVector >> 8);

        var resetVectorOffset = 16 + 0x3FFC;
        rom[resetVectorOffset] = (byte)(resetVector & 0x00FF);
        rom[resetVectorOffset + 1] = (byte)(resetVector >> 8);

        var irqVectorOffset = 16 + 0x3FFE;
        rom[irqVectorOffset] = (byte)(irqVector & 0x00FF);
        rom[irqVectorOffset + 1] = (byte)(irqVector >> 8);

        return INesRomLoader.Load(rom);
    }
}
