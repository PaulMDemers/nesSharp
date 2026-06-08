using NesSharp.Core.Memory;

namespace NesSharp.Core.Cpu;

public sealed class Cpu6502
{
    private const byte CarryFlag = (byte)ProcessorStatus.Carry;
    private const byte ZeroFlag = (byte)ProcessorStatus.Zero;
    private const byte InterruptDisableFlag = (byte)ProcessorStatus.InterruptDisable;
    private const byte DecimalFlag = (byte)ProcessorStatus.Decimal;
    private const byte BreakFlag = (byte)ProcessorStatus.Break;
    private const byte UnusedFlag = (byte)ProcessorStatus.Unused;
    private const byte OverflowFlag = (byte)ProcessorStatus.Overflow;
    private const byte NegativeFlag = (byte)ProcessorStatus.Negative;

    private readonly CpuBus bus;
    private bool nmiRequested;

    public Cpu6502(CpuBus bus)
    {
        this.bus = bus;
    }

    public byte A { get; private set; }

    public byte X { get; private set; }

    public byte Y { get; private set; }

    public byte StackPointer { get; private set; }

    public byte Status { get; private set; }

    public ushort ProgramCounter { get; private set; }

    public ulong Cycles { get; private set; }

    public void Reset()
    {
        nmiRequested = false;
        A = 0;
        X = 0;
        Y = 0;
        StackPointer = 0xFD;
        Status = UnusedFlag | InterruptDisableFlag;
        ProgramCounter = bus.ReadWordRaw(0xFFFC);
        Cycles = 7;
    }

    public void RequestNmi()
    {
        nmiRequested = true;
    }

    public void ClearPendingNmi()
    {
        nmiRequested = false;
    }

    public void SetProgramCounter(ushort programCounter)
    {
        ProgramCounter = programCounter;
    }

    public void SetCycles(ulong cycles)
    {
        Cycles = cycles;
    }

    public CpuTraceState CaptureTraceState()
    {
        var opcode = bus.ReadRaw(ProgramCounter);
        var length = GetInstructionLength(opcode);
        Span<byte> bytes = stackalloc byte[3];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = bus.ReadRaw((ushort)(ProgramCounter + i));
        }

        return new CpuTraceState(
            ProgramCounter,
            bytes[0],
            bytes[1],
            bytes[2],
            length,
            A,
            X,
            Y,
            Status,
            StackPointer,
            Cycles);
    }

    public int Step()
    {
        bus.BeginCpuInstruction();
        try
        {
        if (nmiRequested)
        {
            nmiRequested = false;
            ServiceNmi();
            Cycles += 7;
            return 7;
        }

        var opcode = ReadByte();
        var cycles = Execute(opcode);
        Cycles += (uint)cycles;
        Status |= UnusedFlag;
        return cycles;
        }
        finally
        {
            bus.EndCpuInstruction();
        }
    }

    private int Execute(byte opcode)
    {
        switch (opcode)
        {
            case 0x00: Brk(); return 7;
            case 0x01: Ora(Read(IndexedIndirect())); return 6;
            case 0x03: Slo(IndexedIndirect()); return 8;
            case 0x04: ZeroPage(); return 3;
            case 0x05: Ora(Read(ZeroPage())); return 3;
            case 0x06: AslMemory(ZeroPage()); return 5;
            case 0x07: Slo(ZeroPage()); return 5;
            case 0x08: Push((byte)(Status | BreakFlag | UnusedFlag)); return 3;
            case 0x09: Ora(ReadByte()); return 2;
            case 0x0A: A = Asl(A); return 2;
            case 0x0B: Anc(ReadByte()); return 2;
            case 0x0C: Absolute(); return 4;
            case 0x0D: Ora(Read(Absolute())); return 4;
            case 0x0E: AslMemory(Absolute()); return 6;
            case 0x0F: Slo(Absolute()); return 6;
            case 0x10: return Branch(!GetFlag(NegativeFlag));
            case 0x11: { var (address, crossed) = IndirectIndexed(); Ora(Read(address)); return 5 + crossed; }
            case 0x13: Slo(IndirectIndexedAddress()); return 8;
            case 0x14: ZeroPageX(); return 4;
            case 0x15: Ora(Read(ZeroPageX())); return 4;
            case 0x16: AslMemory(ZeroPageX()); return 6;
            case 0x17: Slo(ZeroPageX()); return 6;
            case 0x18: SetFlag(CarryFlag, false); return 2;
            case 0x19: { var (address, crossed) = AbsoluteY(); Ora(Read(address)); return 4 + crossed; }
            case 0x1A: return 2;
            case 0x1B: Slo(AbsoluteYAddress()); return 7;
            case 0x1C: { var (_, crossed) = AbsoluteX(); return 4 + crossed; }
            case 0x1D: { var (address, crossed) = AbsoluteX(); Ora(Read(address)); return 4 + crossed; }
            case 0x1E: AslMemory(AbsoluteXAddress()); return 7;
            case 0x1F: Slo(AbsoluteXAddress()); return 7;
            case 0x20: Jsr(); return 6;
            case 0x21: And(Read(IndexedIndirect())); return 6;
            case 0x23: Rla(IndexedIndirect()); return 8;
            case 0x24: Bit(Read(ZeroPage())); return 3;
            case 0x25: And(Read(ZeroPage())); return 3;
            case 0x26: RolMemory(ZeroPage()); return 5;
            case 0x27: Rla(ZeroPage()); return 5;
            case 0x28: Status = (byte)((Pull() & ~BreakFlag) | UnusedFlag); return 4;
            case 0x29: And(ReadByte()); return 2;
            case 0x2A: A = Rol(A); return 2;
            case 0x2B: Anc(ReadByte()); return 2;
            case 0x2C: Bit(Read(Absolute())); return 4;
            case 0x2D: And(Read(Absolute())); return 4;
            case 0x2E: RolMemory(Absolute()); return 6;
            case 0x2F: Rla(Absolute()); return 6;
            case 0x30: return Branch(GetFlag(NegativeFlag));
            case 0x31: { var (address, crossed) = IndirectIndexed(); And(Read(address)); return 5 + crossed; }
            case 0x33: Rla(IndirectIndexedAddress()); return 8;
            case 0x34: ZeroPageX(); return 4;
            case 0x35: And(Read(ZeroPageX())); return 4;
            case 0x36: RolMemory(ZeroPageX()); return 6;
            case 0x37: Rla(ZeroPageX()); return 6;
            case 0x38: SetFlag(CarryFlag, true); return 2;
            case 0x39: { var (address, crossed) = AbsoluteY(); And(Read(address)); return 4 + crossed; }
            case 0x3A: return 2;
            case 0x3B: Rla(AbsoluteYAddress()); return 7;
            case 0x3C: { var (_, crossed) = AbsoluteX(); return 4 + crossed; }
            case 0x3D: { var (address, crossed) = AbsoluteX(); And(Read(address)); return 4 + crossed; }
            case 0x3E: RolMemory(AbsoluteXAddress()); return 7;
            case 0x3F: Rla(AbsoluteXAddress()); return 7;
            case 0x40: Rti(); return 6;
            case 0x41: Eor(Read(IndexedIndirect())); return 6;
            case 0x43: Sre(IndexedIndirect()); return 8;
            case 0x44: ZeroPage(); return 3;
            case 0x45: Eor(Read(ZeroPage())); return 3;
            case 0x46: LsrMemory(ZeroPage()); return 5;
            case 0x47: Sre(ZeroPage()); return 5;
            case 0x48: Push(A); return 3;
            case 0x49: Eor(ReadByte()); return 2;
            case 0x4A: A = Lsr(A); return 2;
            case 0x4B: A = (byte)(A & ReadByte()); A = Lsr(A); return 2;
            case 0x4C: ProgramCounter = Absolute(); return 3;
            case 0x4D: Eor(Read(Absolute())); return 4;
            case 0x4E: LsrMemory(Absolute()); return 6;
            case 0x4F: Sre(Absolute()); return 6;
            case 0x50: return Branch(!GetFlag(OverflowFlag));
            case 0x51: { var (address, crossed) = IndirectIndexed(); Eor(Read(address)); return 5 + crossed; }
            case 0x53: Sre(IndirectIndexedAddress()); return 8;
            case 0x54: ZeroPageX(); return 4;
            case 0x55: Eor(Read(ZeroPageX())); return 4;
            case 0x56: LsrMemory(ZeroPageX()); return 6;
            case 0x57: Sre(ZeroPageX()); return 6;
            case 0x58: SetFlag(InterruptDisableFlag, false); return 2;
            case 0x59: { var (address, crossed) = AbsoluteY(); Eor(Read(address)); return 4 + crossed; }
            case 0x5A: return 2;
            case 0x5B: Sre(AbsoluteYAddress()); return 7;
            case 0x5C: { var (_, crossed) = AbsoluteX(); return 4 + crossed; }
            case 0x5D: { var (address, crossed) = AbsoluteX(); Eor(Read(address)); return 4 + crossed; }
            case 0x5E: LsrMemory(AbsoluteXAddress()); return 7;
            case 0x5F: Sre(AbsoluteXAddress()); return 7;
            case 0x60: Rts(); return 6;
            case 0x61: Adc(Read(IndexedIndirect())); return 6;
            case 0x63: Rra(IndexedIndirect()); return 8;
            case 0x64: ZeroPage(); return 3;
            case 0x65: Adc(Read(ZeroPage())); return 3;
            case 0x66: RorMemory(ZeroPage()); return 5;
            case 0x67: Rra(ZeroPage()); return 5;
            case 0x68: A = Pull(); SetZeroNegative(A); return 4;
            case 0x69: Adc(ReadByte()); return 2;
            case 0x6A: A = Ror(A); return 2;
            case 0x6B: Arr(ReadByte()); return 2;
            case 0x6C: ProgramCounter = ReadWordBug(Absolute()); return 5;
            case 0x6D: Adc(Read(Absolute())); return 4;
            case 0x6E: RorMemory(Absolute()); return 6;
            case 0x6F: Rra(Absolute()); return 6;
            case 0x70: return Branch(GetFlag(OverflowFlag));
            case 0x71: { var (address, crossed) = IndirectIndexed(); Adc(Read(address)); return 5 + crossed; }
            case 0x73: Rra(IndirectIndexedAddress()); return 8;
            case 0x74: ZeroPageX(); return 4;
            case 0x75: Adc(Read(ZeroPageX())); return 4;
            case 0x76: RorMemory(ZeroPageX()); return 6;
            case 0x77: Rra(ZeroPageX()); return 6;
            case 0x78: SetFlag(InterruptDisableFlag, true); return 2;
            case 0x79: { var (address, crossed) = AbsoluteY(); Adc(Read(address)); return 4 + crossed; }
            case 0x7A: return 2;
            case 0x7B: Rra(AbsoluteYAddress()); return 7;
            case 0x7C: { var (_, crossed) = AbsoluteX(); return 4 + crossed; }
            case 0x7D: { var (address, crossed) = AbsoluteX(); Adc(Read(address)); return 4 + crossed; }
            case 0x7E: RorMemory(AbsoluteXAddress()); return 7;
            case 0x7F: Rra(AbsoluteXAddress()); return 7;
            case 0x80: ReadByte(); return 2;
            case 0x81: Write(IndexedIndirect(), A); return 6;
            case 0x82: ReadByte(); return 2;
            case 0x83: Sax(IndexedIndirect()); return 6;
            case 0x84: Write(ZeroPage(), Y); return 3;
            case 0x85: Write(ZeroPage(), A); return 3;
            case 0x86: Write(ZeroPage(), X); return 3;
            case 0x87: Sax(ZeroPage()); return 3;
            case 0x88: Y--; SetZeroNegative(Y); return 2;
            case 0x89: ReadByte(); return 2;
            case 0x8A: A = X; SetZeroNegative(A); return 2;
            case 0x8B: A = (byte)((A | 0xEE) & X & ReadByte()); SetZeroNegative(A); return 2;
            case 0x8C: Write(Absolute(), Y); return 4;
            case 0x8D: Write(Absolute(), A); return 4;
            case 0x8E: Write(Absolute(), X); return 4;
            case 0x8F: Sax(Absolute()); return 4;
            case 0x90: return Branch(!GetFlag(CarryFlag));
            case 0x91: StoreIndirectIndexed(A); return 6;
            case 0x93:
            {
                var address = IndirectIndexedAddress();
                Write(address, (byte)(A & X & ((address >> 8) + 1)));
                return 6;
            }
            case 0x94: Write(ZeroPageX(), Y); return 4;
            case 0x95: Write(ZeroPageX(), A); return 4;
            case 0x96: Write(ZeroPageY(), X); return 4;
            case 0x97: Sax(ZeroPageY()); return 4;
            case 0x98: A = Y; SetZeroNegative(A); return 2;
            case 0x99: StoreAbsoluteY(A); return 5;
            case 0x9A: StackPointer = X; return 2;
            case 0x9B: Tas(); return 5;
            case 0x9C: Shy(); return 5;
            case 0x9D: StoreAbsoluteX(A); return 5;
            case 0x9E: Shx(); return 5;
            case 0x9F:
            {
                var address = AbsoluteYAddress();
                Write(address, (byte)(A & X & ((address >> 8) + 1)));
                return 5;
            }
            case 0xA0: Y = ReadByte(); SetZeroNegative(Y); return 2;
            case 0xA1: A = Read(IndexedIndirect()); SetZeroNegative(A); return 6;
            case 0xA2: X = ReadByte(); SetZeroNegative(X); return 2;
            case 0xA3: Lax(Read(IndexedIndirect())); return 6;
            case 0xA4: Y = Read(ZeroPage()); SetZeroNegative(Y); return 3;
            case 0xA5: A = Read(ZeroPage()); SetZeroNegative(A); return 3;
            case 0xA6: X = Read(ZeroPage()); SetZeroNegative(X); return 3;
            case 0xA7: Lax(Read(ZeroPage())); return 3;
            case 0xA8: Y = A; SetZeroNegative(Y); return 2;
            case 0xA9: A = ReadByte(); SetZeroNegative(A); return 2;
            case 0xAA: X = A; SetZeroNegative(X); return 2;
            case 0xAB: Lax(ReadByte()); return 2;
            case 0xAC: Y = Read(Absolute()); SetZeroNegative(Y); return 4;
            case 0xAD: A = Read(Absolute()); SetZeroNegative(A); return 4;
            case 0xAE: X = Read(Absolute()); SetZeroNegative(X); return 4;
            case 0xAF: Lax(Read(Absolute())); return 4;
            case 0xB0: return Branch(GetFlag(CarryFlag));
            case 0xB1: { var (address, crossed) = IndirectIndexed(); A = Read(address); SetZeroNegative(A); return 5 + crossed; }
            case 0xB3: { var (address, crossed) = IndirectIndexed(); Lax(Read(address)); return 5 + crossed; }
            case 0xB4: Y = Read(ZeroPageX()); SetZeroNegative(Y); return 4;
            case 0xB5: A = Read(ZeroPageX()); SetZeroNegative(A); return 4;
            case 0xB6: X = Read(ZeroPageY()); SetZeroNegative(X); return 4;
            case 0xB7: Lax(Read(ZeroPageY())); return 4;
            case 0xB8: SetFlag(OverflowFlag, false); return 2;
            case 0xB9: { var (address, crossed) = AbsoluteY(); A = Read(address); SetZeroNegative(A); return 4 + crossed; }
            case 0xBA: X = StackPointer; SetZeroNegative(X); return 2;
            case 0xBB: { var (address, crossed) = AbsoluteY(); Las(Read(address)); return 4 + crossed; }
            case 0xBC: { var (address, crossed) = AbsoluteX(); Y = Read(address); SetZeroNegative(Y); return 4 + crossed; }
            case 0xBD: { var (address, crossed) = AbsoluteX(); A = Read(address); SetZeroNegative(A); return 4 + crossed; }
            case 0xBE: { var (address, crossed) = AbsoluteY(); X = Read(address); SetZeroNegative(X); return 4 + crossed; }
            case 0xBF: { var (address, crossed) = AbsoluteY(); Lax(Read(address)); return 4 + crossed; }
            case 0xC0: Compare(Y, ReadByte()); return 2;
            case 0xC1: Compare(A, Read(IndexedIndirect())); return 6;
            case 0xC2: ReadByte(); return 2;
            case 0xC3: Dcp(IndexedIndirect()); return 8;
            case 0xC4: Compare(Y, Read(ZeroPage())); return 3;
            case 0xC5: Compare(A, Read(ZeroPage())); return 3;
            case 0xC6: DecMemory(ZeroPage()); return 5;
            case 0xC7: Dcp(ZeroPage()); return 5;
            case 0xC8: Y++; SetZeroNegative(Y); return 2;
            case 0xC9: Compare(A, ReadByte()); return 2;
            case 0xCA: X--; SetZeroNegative(X); return 2;
            case 0xCB: Axs(ReadByte()); return 2;
            case 0xCC: Compare(Y, Read(Absolute())); return 4;
            case 0xCD: Compare(A, Read(Absolute())); return 4;
            case 0xCE: DecMemory(Absolute()); return 6;
            case 0xCF: Dcp(Absolute()); return 6;
            case 0xD0: return Branch(!GetFlag(ZeroFlag));
            case 0xD1: { var (address, crossed) = IndirectIndexed(); Compare(A, Read(address)); return 5 + crossed; }
            case 0xD3: Dcp(IndirectIndexedAddress()); return 8;
            case 0xD4: ZeroPageX(); return 4;
            case 0xD5: Compare(A, Read(ZeroPageX())); return 4;
            case 0xD6: DecMemory(ZeroPageX()); return 6;
            case 0xD7: Dcp(ZeroPageX()); return 6;
            case 0xD8: SetFlag(DecimalFlag, false); return 2;
            case 0xD9: { var (address, crossed) = AbsoluteY(); Compare(A, Read(address)); return 4 + crossed; }
            case 0xDA: return 2;
            case 0xDB: Dcp(AbsoluteYAddress()); return 7;
            case 0xDC: { var (_, crossed) = AbsoluteX(); return 4 + crossed; }
            case 0xDD: { var (address, crossed) = AbsoluteX(); Compare(A, Read(address)); return 4 + crossed; }
            case 0xDE: DecMemory(AbsoluteXAddress()); return 7;
            case 0xDF: Dcp(AbsoluteXAddress()); return 7;
            case 0xE0: Compare(X, ReadByte()); return 2;
            case 0xE1: Sbc(Read(IndexedIndirect())); return 6;
            case 0xE2: ReadByte(); return 2;
            case 0xE3: Isc(IndexedIndirect()); return 8;
            case 0xE4: Compare(X, Read(ZeroPage())); return 3;
            case 0xE5: Sbc(Read(ZeroPage())); return 3;
            case 0xE6: IncMemory(ZeroPage()); return 5;
            case 0xE7: Isc(ZeroPage()); return 5;
            case 0xE8: X++; SetZeroNegative(X); return 2;
            case 0xE9: Sbc(ReadByte()); return 2;
            case 0xEA: return 2;
            case 0xEB: Sbc(ReadByte()); return 2;
            case 0xEC: Compare(X, Read(Absolute())); return 4;
            case 0xED: Sbc(Read(Absolute())); return 4;
            case 0xEE: IncMemory(Absolute()); return 6;
            case 0xEF: Isc(Absolute()); return 6;
            case 0xF0: return Branch(GetFlag(ZeroFlag));
            case 0xF1: { var (address, crossed) = IndirectIndexed(); Sbc(Read(address)); return 5 + crossed; }
            case 0xF3: Isc(IndirectIndexedAddress()); return 8;
            case 0xF4: ZeroPageX(); return 4;
            case 0xF5: Sbc(Read(ZeroPageX())); return 4;
            case 0xF6: IncMemory(ZeroPageX()); return 6;
            case 0xF7: Isc(ZeroPageX()); return 6;
            case 0xF8: SetFlag(DecimalFlag, true); return 2;
            case 0xF9: { var (address, crossed) = AbsoluteY(); Sbc(Read(address)); return 4 + crossed; }
            case 0xFA: return 2;
            case 0xFB: Isc(AbsoluteYAddress()); return 7;
            case 0xFC: { var (_, crossed) = AbsoluteX(); return 4 + crossed; }
            case 0xFD: { var (address, crossed) = AbsoluteX(); Sbc(Read(address)); return 4 + crossed; }
            case 0xFE: IncMemory(AbsoluteXAddress()); return 7;
            case 0xFF: Isc(AbsoluteXAddress()); return 7;
            default:
                throw new InvalidOperationException($"Opcode ${opcode:X2} is not implemented.");
        }
    }

    private static int GetInstructionLength(byte opcode)
    {
        return opcode switch
        {
            0x00 or 0x08 or 0x0A or 0x18 or 0x28 or 0x2A or 0x38 or 0x40 or 0x48 or 0x4A or 0x58 or 0x60 or
            0x68 or 0x6A or 0x78 or 0x88 or 0x8A or 0x98 or 0x9A or 0xA8 or 0xAA or 0xB8 or 0xBA or 0xC8 or
            0xCA or 0xD8 or 0xE8 or 0xEA or 0xF8 => 1,
            0x0C or 0x0D or 0x0E or 0x0F or 0x19 or 0x1B or 0x1C or 0x1D or 0x1E or 0x1F or 0x20 or 0x2C or
            0x2D or 0x2E or 0x2F or 0x39 or 0x3B or 0x3C or 0x3D or 0x3E or 0x3F or 0x4C or 0x4D or 0x4E or
            0x4F or 0x59 or 0x5B or 0x5C or 0x5D or 0x5E or 0x5F or 0x6C or 0x6D or 0x6E or 0x6F or 0x79 or
            0x7B or 0x7C or 0x7D or 0x7E or 0x7F or 0x8C or 0x8D or 0x8E or 0x8F or 0x99 or 0x9B or 0x9C or
            0x9D or 0x9E or 0x9F or 0xAC or 0xAD or 0xAE or 0xAF or 0xB9 or 0xBB or 0xBC or 0xBD or 0xBE or
            0xBF or 0xCC or 0xCD or 0xCE or 0xCF or 0xD9 or 0xDB or 0xDC or 0xDD or 0xDE or 0xDF or 0xEC or
            0xED or 0xEE or 0xEF or 0xF9 or 0xFB or 0xFC or 0xFD or 0xFE or 0xFF => 3,
            _ => 2
        };
    }

    private byte ReadByte()
    {
        return Read(ProgramCounter++);
    }

    private ushort ReadWord()
    {
        var low = ReadByte();
        var high = ReadByte();
        return (ushort)(low | (high << 8));
    }

    private byte Read(ushort address) => bus.Read(address);

    private void Write(ushort address, byte value) => bus.Write(address, value);

    private ushort Absolute() => ReadWord();

    private (ushort Address, int PageCrossed) AbsoluteX()
    {
        var baseAddress = Absolute();
        var address = (ushort)(baseAddress + X);
        return (address, PageCrossed(baseAddress, address));
    }

    private ushort AbsoluteXAddress() => (ushort)(Absolute() + X);

    private (ushort Address, int PageCrossed) AbsoluteY()
    {
        var baseAddress = Absolute();
        var address = (ushort)(baseAddress + Y);
        return (address, PageCrossed(baseAddress, address));
    }

    private ushort AbsoluteYAddress() => (ushort)(Absolute() + Y);

    private ushort ZeroPage() => ReadByte();

    private ushort ZeroPageX() => (byte)(ReadByte() + X);

    private ushort ZeroPageY() => (byte)(ReadByte() + Y);

    private ushort IndexedIndirect()
    {
        var pointer = (byte)(ReadByte() + X);
        var low = Read(pointer);
        var high = Read((byte)(pointer + 1));
        return (ushort)(low | (high << 8));
    }

    private (ushort Address, int PageCrossed) IndirectIndexed()
    {
        var pointer = ReadByte();
        var low = Read(pointer);
        var high = Read((byte)(pointer + 1));
        var baseAddress = (ushort)(low | (high << 8));
        var address = (ushort)(baseAddress + Y);
        return (address, PageCrossed(baseAddress, address));
    }

    private ushort IndirectIndexedAddress()
    {
        var pointer = ReadByte();
        var low = Read(pointer);
        var high = Read((byte)(pointer + 1));
        return (ushort)((low | (high << 8)) + Y);
    }

    private ushort ReadWordBug(ushort address)
    {
        var low = Read(address);
        var highAddress = (ushort)((address & 0xFF00) | ((address + 1) & 0x00FF));
        var high = Read(highAddress);
        return (ushort)(low | (high << 8));
    }

    private static int PageCrossed(ushort from, ushort to) => (from & 0xFF00) == (to & 0xFF00) ? 0 : 1;

    private int Branch(bool condition)
    {
        var offset = unchecked((sbyte)ReadByte());
        if (!condition)
        {
            return 2;
        }

        var oldPc = ProgramCounter;
        ProgramCounter = (ushort)(ProgramCounter + offset);
        return 3 + PageCrossed(oldPc, ProgramCounter);
    }

    private void Jsr()
    {
        var target = Absolute();
        var returnAddress = (ushort)(ProgramCounter - 1);
        Push((byte)(returnAddress >> 8));
        Push((byte)(returnAddress & 0x00FF));
        ProgramCounter = target;
    }

    private void Rts()
    {
        var low = Pull();
        var high = Pull();
        ProgramCounter = (ushort)(((high << 8) | low) + 1);
    }

    private void Brk()
    {
        ProgramCounter++;
        Push((byte)(ProgramCounter >> 8));
        Push((byte)(ProgramCounter & 0x00FF));
        Push((byte)(Status | BreakFlag | UnusedFlag));
        SetFlag(InterruptDisableFlag, true);
        ProgramCounter = bus.ReadWord(0xFFFE);
    }

    private void Rti()
    {
        Status = (byte)((Pull() & ~BreakFlag) | UnusedFlag);
        var low = Pull();
        var high = Pull();
        ProgramCounter = (ushort)(low | (high << 8));
    }

    private void ServiceNmi()
    {
        Push((byte)(ProgramCounter >> 8));
        Push((byte)(ProgramCounter & 0x00FF));
        Push((byte)((Status & ~BreakFlag) | UnusedFlag));
        SetFlag(InterruptDisableFlag, true);
        ProgramCounter = bus.ReadWord(0xFFFA);
    }

    private void Push(byte value)
    {
        Write((ushort)(0x0100 | StackPointer), value);
        StackPointer--;
    }

    private byte Pull()
    {
        StackPointer++;
        return Read((ushort)(0x0100 | StackPointer));
    }

    private void Adc(byte value)
    {
        var carry = GetFlag(CarryFlag) ? 1 : 0;
        var result = A + value + carry;
        var byteResult = (byte)result;
        SetFlag(CarryFlag, result > 0xFF);
        SetFlag(OverflowFlag, (~(A ^ value) & (A ^ byteResult) & 0x80) != 0);
        A = byteResult;
        SetZeroNegative(A);
    }

    private void Sbc(byte value)
    {
        Adc((byte)~value);
    }

    private void And(byte value)
    {
        A &= value;
        SetZeroNegative(A);
    }

    private void Ora(byte value)
    {
        A |= value;
        SetZeroNegative(A);
    }

    private void Eor(byte value)
    {
        A ^= value;
        SetZeroNegative(A);
    }

    private void Bit(byte value)
    {
        SetFlag(ZeroFlag, (A & value) == 0);
        SetFlag(OverflowFlag, (value & OverflowFlag) != 0);
        SetFlag(NegativeFlag, (value & NegativeFlag) != 0);
    }

    private void Compare(byte register, byte value)
    {
        var result = (byte)(register - value);
        SetFlag(CarryFlag, register >= value);
        SetZeroNegative(result);
    }

    private byte Asl(byte value)
    {
        SetFlag(CarryFlag, (value & 0x80) != 0);
        value <<= 1;
        SetZeroNegative(value);
        return value;
    }

    private void AslMemory(ushort address)
    {
        Write(address, Asl(Read(address)));
    }

    private byte Lsr(byte value)
    {
        SetFlag(CarryFlag, (value & 0x01) != 0);
        value >>= 1;
        SetZeroNegative(value);
        return value;
    }

    private void LsrMemory(ushort address)
    {
        Write(address, Lsr(Read(address)));
    }

    private byte Rol(byte value)
    {
        var oldCarry = GetFlag(CarryFlag);
        SetFlag(CarryFlag, (value & 0x80) != 0);
        value = (byte)((value << 1) | (oldCarry ? 1 : 0));
        SetZeroNegative(value);
        return value;
    }

    private void RolMemory(ushort address)
    {
        Write(address, Rol(Read(address)));
    }

    private byte Ror(byte value)
    {
        var oldCarry = GetFlag(CarryFlag);
        SetFlag(CarryFlag, (value & 0x01) != 0);
        value = (byte)((value >> 1) | (oldCarry ? 0x80 : 0));
        SetZeroNegative(value);
        return value;
    }

    private void RorMemory(ushort address)
    {
        Write(address, Ror(Read(address)));
    }

    private void DecMemory(ushort address)
    {
        var value = (byte)(Read(address) - 1);
        Write(address, value);
        SetZeroNegative(value);
    }

    private void IncMemory(ushort address)
    {
        var value = (byte)(Read(address) + 1);
        Write(address, value);
        SetZeroNegative(value);
    }

    private void StoreWithDummyRead(ushort address, byte value)
    {
        Read(address);
        Write(address, value);
    }

    private void StoreAbsoluteX(byte value)
    {
        var baseAddress = Absolute();
        StoreIndexed(baseAddress, X, value);
    }

    private void StoreAbsoluteY(byte value)
    {
        var baseAddress = Absolute();
        StoreIndexed(baseAddress, Y, value);
    }

    private void StoreIndirectIndexed(byte value)
    {
        var pointer = ReadByte();
        var low = Read(pointer);
        var high = Read((byte)(pointer + 1));
        var baseAddress = (ushort)(low | (high << 8));
        StoreIndexed(baseAddress, Y, value);
    }

    private void StoreIndexed(ushort baseAddress, byte index, byte value)
    {
        var finalAddress = (ushort)(baseAddress + index);
        var dummyAddress = (ushort)((baseAddress & 0xFF00) | ((baseAddress + index) & 0x00FF));
        Read(dummyAddress);
        Write(finalAddress, value);
    }

    private void Slo(ushort address)
    {
        var value = Asl(Read(address));
        Write(address, value);
        Ora(value);
    }

    private void Rla(ushort address)
    {
        var value = Rol(Read(address));
        Write(address, value);
        And(value);
    }

    private void Sre(ushort address)
    {
        var value = Lsr(Read(address));
        Write(address, value);
        Eor(value);
    }

    private void Rra(ushort address)
    {
        var value = Ror(Read(address));
        Write(address, value);
        Adc(value);
    }

    private void Dcp(ushort address)
    {
        var value = (byte)(Read(address) - 1);
        Write(address, value);
        Compare(A, value);
    }

    private void Isc(ushort address)
    {
        var value = (byte)(Read(address) + 1);
        Write(address, value);
        Sbc(value);
    }

    private void Lax(byte value)
    {
        A = value;
        X = value;
        SetZeroNegative(value);
    }

    private void Sax(ushort address)
    {
        Write(address, (byte)(A & X));
    }

    private void Anc(byte value)
    {
        A = (byte)(A & value);
        SetZeroNegative(A);
        SetFlag(CarryFlag, GetFlag(NegativeFlag));
    }

    private void Arr(byte value)
    {
        A = (byte)(A & value);
        A = Ror(A);
        SetFlag(CarryFlag, (A & 0x40) != 0);
        SetFlag(OverflowFlag, (((A >> 6) ^ (A >> 5)) & 0x01) != 0);
        SetZeroNegative(A);
    }

    private void Axs(byte value)
    {
        var source = A & X;
        var result = source - value;
        SetFlag(CarryFlag, result >= 0);
        X = (byte)result;
        SetZeroNegative(X);
    }

    private void Las(byte value)
    {
        var result = (byte)(value & StackPointer);
        A = result;
        X = result;
        StackPointer = result;
        SetZeroNegative(result);
    }

    private void Tas()
    {
        var baseAddress = Absolute();
        var address = (ushort)(baseAddress + Y);
        StackPointer = (byte)(A & X);
        Write(address, (byte)(StackPointer & ((address >> 8) + 1)));
    }

    private void Shy()
    {
        var baseAddress = Absolute();
        var highMask = (byte)(((baseAddress >> 8) + 1) & 0xFF);
        var low = (byte)(baseAddress + X);
        var high = (byte)(baseAddress >> 8);
        if (((baseAddress & 0x00FF) + X) > 0xFF)
        {
            high &= Y;
        }

        Write((ushort)((high << 8) | low), (byte)(Y & highMask));
    }

    private void Shx()
    {
        var baseAddress = Absolute();
        var highMask = (byte)(((baseAddress >> 8) + 1) & 0xFF);
        var low = (byte)(baseAddress + Y);
        var high = (byte)(baseAddress >> 8);
        if (((baseAddress & 0x00FF) + Y) > 0xFF)
        {
            high &= X;
        }

        Write((ushort)((high << 8) | low), (byte)(X & highMask));
    }

    private bool GetFlag(byte flag) => (Status & flag) != 0;

    private void SetFlag(byte flag, bool enabled)
    {
        Status = enabled ? (byte)(Status | flag) : (byte)(Status & ~flag);
    }

    private void SetZeroNegative(byte value)
    {
        SetFlag(ZeroFlag, value == 0);
        SetFlag(NegativeFlag, (value & 0x80) != 0);
    }
}
