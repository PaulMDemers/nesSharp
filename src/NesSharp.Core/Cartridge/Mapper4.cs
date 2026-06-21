namespace NesSharp.Core.Cartridge;

public sealed class Mapper4 : IMapper
{
    private const int PrgBankSize = 8 * 1024;
    private const int ChrBankSize = 1024;
    private const ulong A12LowFilterDots = 0;

    private readonly CartridgeHeader header;
    private readonly byte[] prgRom;
    private readonly byte[] chrMemory;
    private readonly byte[] prgRam;
    private readonly byte[] bankRegisters = new byte[8];
    private byte bankSelect;
    private MirroringMode mirroringMode;
    private bool prgRamEnabled = true;
    private bool prgRamWriteProtected;
    private byte irqLatch;
    private byte irqCounter;
    private bool irqReload;
    private bool irqEnabled;
    private bool irqPending;
    private bool ppuA12High;
    private ulong ppuA12LowSinceDot;

    public Mapper4(CartridgeHeader header, byte[] prgRom, byte[] chrMemory)
    {
        if (header.PrgRomBanks < 1)
        {
            throw new InvalidRomException("Mapper 4 requires at least one PRG ROM bank.");
        }

        this.header = header;
        this.prgRom = prgRom;
        this.chrMemory = chrMemory;
        prgRam = new byte[header.PrgRamSize];
        mirroringMode = header.MirroringMode;
    }

    public MirroringMode CurrentMirroringMode => header.HasFourScreenVram ? MirroringMode.FourScreen : mirroringMode;

    public ReadOnlySpan<byte> SaveRam => prgRam;

    public bool IsIrqPending => irqPending;

    public Mapper4DebugState CaptureDebugState()
    {
        var registers = new byte[bankRegisters.Length];
        bankRegisters.CopyTo(registers, 0);

        var prgBanks = new int[4];
        for (var slot = 0; slot < prgBanks.Length; slot++)
        {
            prgBanks[slot] = GetPrgBankForSlot(slot);
        }

        var chrBanks = new int[8];
        for (var slot = 0; slot < chrBanks.Length; slot++)
        {
            chrBanks[slot] = GetChrBankForSlot(slot);
        }

        return new Mapper4DebugState(
            bankSelect,
            registers,
            IsPrgBankModeInverted,
            IsChrBankModeInverted,
            prgBanks,
            chrBanks,
            irqLatch,
            irqCounter,
            irqReload,
            irqEnabled,
            irqPending,
            ppuA12High,
            prgRamEnabled,
            prgRamWriteProtected,
            mirroringMode);
    }

    public byte CpuRead(ushort address)
    {
        return address switch
        {
            >= 0x6000 and <= 0x7FFF => prgRamEnabled ? prgRam[(address - 0x6000) % prgRam.Length] : (byte)0,
            >= 0x8000 => prgRom[MapPrgRomAddress(address)],
            _ => 0
        };
    }

    public void CpuWrite(ushort address, byte value)
    {
        if (address is >= 0x6000 and <= 0x7FFF)
        {
            if (prgRamEnabled && !prgRamWriteProtected)
            {
                prgRam[(address - 0x6000) % prgRam.Length] = value;
            }

            return;
        }

        if (address < 0x8000)
        {
            return;
        }

        switch (address & 0xE001)
        {
            case 0x8000:
                bankSelect = value;
                break;
            case 0x8001:
                bankRegisters[bankSelect & 0x07] = value;
                break;
            case 0xA000:
                mirroringMode = (value & 0x01) == 0 ? MirroringMode.Horizontal : MirroringMode.Vertical;
                break;
            case 0xA001:
                prgRamEnabled = (value & 0x80) != 0;
                prgRamWriteProtected = (value & 0x40) != 0;
                break;
            case 0xC000:
                irqLatch = value;
                break;
            case 0xC001:
                irqCounter = 0;
                irqReload = true;
                break;
            case 0xE000:
                irqEnabled = false;
                irqPending = false;
                break;
            case 0xE001:
                irqEnabled = true;
                break;
        }
    }

    public byte PpuRead(ushort address)
    {
        NotifyPpuAddress(address, 0);
        return PpuPeek(address);
    }

    public byte PpuPeek(ushort address)
    {
        if (address > 0x1FFF)
        {
            return 0;
        }

        return chrMemory[MapChrAddress(address)];
    }

    public void PpuWrite(ushort address, byte value)
    {
        NotifyPpuAddress(address, 0);
        if (address > 0x1FFF || !header.UsesChrRam)
        {
            return;
        }

        chrMemory[MapChrAddress(address)] = value;
    }

    public void NotifyPpuAddress(ushort address, ulong ppuDot)
    {
        var a12High = (address & 0x1000) != 0;
        if (!a12High)
        {
            if (ppuA12High)
            {
                ppuA12LowSinceDot = ppuDot;
            }

            ppuA12High = false;
            return;
        }

        if (!ppuA12High && ppuDot - ppuA12LowSinceDot >= A12LowFilterDots)
        {
            ClockIrqCounter();
        }

        ppuA12High = true;
    }

    public void LoadSaveRam(ReadOnlySpan<byte> data)
    {
        data[..Math.Min(data.Length, prgRam.Length)].CopyTo(prgRam);
    }

    private bool IsPrgBankModeInverted => (bankSelect & 0x40) != 0;

    private bool IsChrBankModeInverted => (bankSelect & 0x80) != 0;

    private int PrgBankCount => Math.Max(1, prgRom.Length / PrgBankSize);

    private int ChrBankCount => Math.Max(1, chrMemory.Length / ChrBankSize);

    private int MapPrgRomAddress(ushort address)
    {
        var slot = (address - 0x8000) / PrgBankSize;
        var offset = address & (PrgBankSize - 1);
        return GetPrgBankForSlot(slot) * PrgBankSize + offset;
    }

    private int GetPrgBankForSlot(int slot)
    {
        var secondLastBank = PrgBankCount - 2;
        var lastBank = PrgBankCount - 1;
        var bank = slot switch
        {
            0 => IsPrgBankModeInverted ? secondLastBank : bankRegisters[6],
            1 => bankRegisters[7],
            2 => IsPrgBankModeInverted ? bankRegisters[6] : secondLastBank,
            _ => lastBank
        };

        return bank % PrgBankCount;
    }

    private int MapChrAddress(ushort address)
    {
        var slot = address / ChrBankSize;
        var offset = address & (ChrBankSize - 1);
        return GetChrBankForSlot(slot) * ChrBankSize + offset;
    }

    private int GetChrBankForSlot(int slot)
    {
        var bank = IsChrBankModeInverted
            ? slot switch
            {
                0 => bankRegisters[2],
                1 => bankRegisters[3],
                2 => bankRegisters[4],
                3 => bankRegisters[5],
                4 => bankRegisters[0] & 0xFE,
                5 => (bankRegisters[0] & 0xFE) + 1,
                6 => bankRegisters[1] & 0xFE,
                _ => (bankRegisters[1] & 0xFE) + 1
            }
            : slot switch
            {
                0 => bankRegisters[0] & 0xFE,
                1 => (bankRegisters[0] & 0xFE) + 1,
                2 => bankRegisters[1] & 0xFE,
                3 => (bankRegisters[1] & 0xFE) + 1,
                4 => bankRegisters[2],
                5 => bankRegisters[3],
                6 => bankRegisters[4],
                _ => bankRegisters[5]
            };

        return bank % ChrBankCount;
    }

    private void ClockIrqCounter()
    {
        if (irqCounter == 0 || irqReload)
        {
            irqCounter = irqLatch;
            irqReload = false;
        }
        else
        {
            irqCounter--;
        }

        if (irqCounter == 0 && irqEnabled)
        {
            irqPending = true;
        }
    }
}

public readonly record struct Mapper4DebugState(
    byte BankSelect,
    byte[] BankRegisters,
    bool IsPrgBankModeInverted,
    bool IsChrBankModeInverted,
    int[] PrgBanks,
    int[] ChrBanks,
    byte IrqLatch,
    byte IrqCounter,
    bool IrqReload,
    bool IrqEnabled,
    bool IrqPending,
    bool PpuA12High,
    bool PrgRamEnabled,
    bool PrgRamWriteProtected,
    MirroringMode MirroringMode);
