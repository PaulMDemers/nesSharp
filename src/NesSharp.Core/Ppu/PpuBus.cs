using NesSharp.Core.Cartridge;

namespace NesSharp.Core.Ppu;

public sealed class PpuBus
{
    private const byte VblankFlag = 0x80;
    private const byte SpriteZeroHitFlag = 0x40;
    private const byte SpriteOverflowFlag = 0x20;
    private const byte NmiEnableFlag = 0x80;
    private const int DotsPerScanline = 341;
    private const int ScanlinesPerFrame = 262;
    private const int ScreenWidth = 256;
    private const int ScreenHeight = 240;
    private const ulong OpenBusDecayDots = 60UL * DotsPerScanline * ScanlinesPerFrame;

    private readonly Cartridge.Cartridge cartridge;
    private readonly byte[] nametableRam = new byte[2 * 1024];
    private readonly byte[] paletteRam = new byte[32];
    private readonly byte[] oam = new byte[256];
    private readonly byte[] framebuffer = new byte[ScreenWidth * ScreenHeight];
    private readonly byte[] registers = new byte[8];
    private readonly SpriteRenderEntry[] scanlineSprites = new SpriteRenderEntry[8];
    private byte effectiveMask;
    private byte pendingMask;
    private int pendingMaskDelayDots;
    private bool nmiPending;
    private int nmiDelayDots;
    private int nmiPollDelay;
    private bool suppressVblankSet;
    private byte oamAddress;
    private byte openBus;
    private ulong openBusHighUpdatedAt;
    private ulong openBusLowUpdatedAt;
    private byte readBuffer;
    private byte fineX;
    private int scrollX;
    private int scrollY;
    private ushort currentVramAddress;
    private ushort temporaryVramAddress;
    private bool writeToggle;
    private bool renderBackgroundFromCurrentVramAddress;
    private BackgroundFetchPipeline backgroundFetchPipeline;
    private BackgroundShiftRegister backgroundShiftRegister;
    private int scanlineSpriteY = -1;
    private int scanlineSpriteCount;
    private ulong vblankSetTotalDots;

    public PpuBus(Cartridge.Cartridge cartridge)
    {
        this.cartridge = cartridge;
    }

    public int Dot { get; private set; }

    public int Scanline { get; private set; }

    public ulong Frame { get; private set; }

    public ulong TotalDots { get; private set; }

    public event Action? NmiSuppressed;

    public ReadOnlySpan<byte> Framebuffer => framebuffer;

    public byte ReadRegister(ushort address)
    {
        var register = address & 0x0007;
        if (register != 2)
        {
            return register switch
            {
                4 => ReadOamData(),
                7 => ReadPpuData(),
                _ => GetOpenBus()
            };
        }

        ApplyStatusReadVblankSuppression();
        var lowOpenBus = (byte)(GetOpenBus() & 0x1F);
        var status = (byte)((registers[2] & 0xE0) | lowOpenBus);
        SetOpenBus((byte)((registers[2] & 0xE0) | lowOpenBus), refreshLowBits: false);
        registers[2] = (byte)(registers[2] & ~VblankFlag);
        writeToggle = false;
        return status;
    }

    public void WriteRegister(ushort address, byte value)
    {
        var register = address & 0x0007;
        SetOpenBus(value);
        switch (register)
        {
            case 0:
            {
                var wasNmiEnabled = IsNmiEnabled;
                registers[0] = value;
                temporaryVramAddress = (ushort)((temporaryVramAddress & 0xF3FF) | ((value & 0x03) << 10));
                if (!wasNmiEnabled && IsNmiEnabled && CanTriggerImmediateNmi)
                {
                    nmiPending = true;
                    nmiDelayDots = 0;
                    nmiPollDelay = 1;
                }
                else if (wasNmiEnabled && !IsNmiEnabled && CanCancelVblankStartNmi)
                {
                    nmiPending = false;
                    nmiDelayDots = 0;
                    nmiPollDelay = 0;
                    NmiSuppressed?.Invoke();
                }

                break;
            }
            case 2:
                break;
            case 3:
                oamAddress = value;
                registers[register] = value;
                break;
            case 4:
                oam[oamAddress++] = value;
                registers[register] = value;
                break;
            case 5:
                WriteScroll(value);
                registers[register] = value;
                break;
            case 6:
                WriteAddress(value);
                registers[register] = value;
                break;
            case 7:
                WritePpuData(value);
                registers[register] = value;
                break;
            case 1:
                registers[register] = value;
                pendingMask = value;
                // PPUMASK rendering bits take effect a couple of dots after the CPU write.
                pendingMaskDelayDots = 2;
                break;
            default:
                registers[register] = value;
                break;
        }
    }

    public void Reset()
    {
        Array.Clear(registers);
        Array.Clear(nametableRam);
        Array.Clear(paletteRam);
        Array.Clear(oam);
        Array.Clear(framebuffer);
        effectiveMask = 0;
        pendingMask = 0;
        pendingMaskDelayDots = 0;
        Dot = 0;
        Scanline = 0;
        Frame = 0;
        TotalDots = 0;
        nmiPending = false;
        nmiDelayDots = 0;
        nmiPollDelay = 0;
        suppressVblankSet = false;
        oamAddress = 0;
        openBus = 0;
        openBusHighUpdatedAt = 0;
        openBusLowUpdatedAt = 0;
        readBuffer = 0;
        fineX = 0;
        scrollX = 0;
        scrollY = 0;
        currentVramAddress = 0;
        temporaryVramAddress = 0;
        writeToggle = false;
        renderBackgroundFromCurrentVramAddress = false;
        backgroundFetchPipeline = default;
        backgroundShiftRegister = default;
        scanlineSpriteY = -1;
        scanlineSpriteCount = 0;
        vblankSetTotalDots = 0;
    }

    public void Clock(int ppuCycles)
    {
        for (var i = 0; i < ppuCycles; i++)
        {
            TickNmiDelay();

            if (Scanline == 261 && Dot == 339 && IsOddFrame && IsRenderingEnabled)
            {
                TotalDots++;
                Dot = 0;
                Scanline = 0;
                Frame++;
                TickMaskDelay();
                continue;
            }

            TotalDots++;
            Dot++;
            if (Dot >= DotsPerScanline)
            {
                Dot = 0;
                Scanline++;
                if (Scanline >= ScanlinesPerFrame)
                {
                    Scanline = 0;
                    Frame++;
                }
            }

            if (Scanline == 241 && Dot == 1)
            {
                if (suppressVblankSet)
                {
                    suppressVblankSet = false;
                }
                else
                {
                    SetVblank();
                }
            }
            else if (Scanline == 261 && Dot == 1)
            {
                ClearRenderingFlags();
            }

            EvaluateSpriteOverflow();
            RunBackgroundFetchStep();
            RunSpritePatternFetchStep();
            RenderCurrentPixel();
            ShiftBackgroundRegisters();
            EvaluateSpriteZeroHit();
            UpdateRenderingVramAddress();
            TickMaskDelay();
        }
    }

    private void RunBackgroundFetchStep()
    {
        if (!IsRenderingEnabled ||
            !IsRenderingScanline ||
            Dot is not (>= 1 and <= 256 or >= 321 and <= 336))
        {
            return;
        }

        // MMC3 clocks from the rising edge of PPU A12 during rendered fetches.
        // This approximates the first high background fetch in both normal and inverted pattern-table modes.
        if (Dot is 1 or 321 && (registers[0] & 0x10) != 0)
        {
            cartridge.NotifyPpuAddress(0x1000);
        }

        switch (Dot & 0x07)
        {
            case 1:
                backgroundFetchPipeline = new BackgroundFetchPipeline(
                    IsValid: true,
                    RenderAddress: currentVramAddress,
                    TileIndex: FetchBackgroundNametableByte(currentVramAddress),
                    AttributePalette: 0,
                    PatternAddress: 0,
                    PatternLow: 0,
                    PatternHigh: 0);
                break;
            case 3:
                if (!backgroundFetchPipeline.IsValid)
                {
                    break;
                }

                backgroundFetchPipeline = backgroundFetchPipeline with
                {
                    AttributePalette = FetchBackgroundAttributePalette(backgroundFetchPipeline.RenderAddress)
                };
                break;
            case 5:
                if (!backgroundFetchPipeline.IsValid)
                {
                    break;
                }

                var fineY = (backgroundFetchPipeline.RenderAddress >> 12) & 0x07;
                var patternAddress = GetBackgroundPatternAddress(backgroundFetchPipeline.TileIndex, fineY);
                backgroundFetchPipeline = backgroundFetchPipeline with
                {
                    PatternAddress = patternAddress,
                    PatternLow = FetchBackgroundPatternLow(patternAddress)
                };
                break;
            case 7:
                if (!backgroundFetchPipeline.IsValid)
                {
                    break;
                }

                backgroundFetchPipeline = backgroundFetchPipeline with
                {
                    PatternHigh = FetchBackgroundPatternHigh(backgroundFetchPipeline.PatternAddress)
                };
                LoadBackgroundShiftRegister();
                break;
        }
    }

    private void RunSpritePatternFetchStep()
    {
        if (!IsRenderingEnabled ||
            !IsRenderingScanline ||
            Dot != 257)
        {
            return;
        }

        var nextScanline = Scanline == 261 ? 0 : Scanline + 1;
        if (nextScanline is >= 0 and < ScreenHeight)
        {
            LoadSpriteScanline(nextScanline);
        }
        else
        {
            scanlineSpriteY = nextScanline;
            scanlineSpriteCount = 0;
        }

        if (IsSpriteRenderingEnabled)
        {
            var spritePatternBase = (registers[0] & 0x08) == 0 ? 0x0000 : 0x1000;
            cartridge.NotifyPpuAddress((ushort)spritePatternBase);
        }
    }

    private void LoadBackgroundShiftRegister()
    {
        if (!backgroundShiftRegister.IsValid)
        {
            backgroundShiftRegister = new BackgroundShiftRegister(
                true,
                backgroundFetchPipeline.RenderAddress,
                NextRenderAddress: 0,
                PatternLow: (ushort)(backgroundFetchPipeline.PatternLow << 8),
                PatternHigh: (ushort)(backgroundFetchPipeline.PatternHigh << 8),
                AttributeLow: GetAttributeShiftPlane(backgroundFetchPipeline.AttributePalette, 0, highByte: true),
                AttributeHigh: GetAttributeShiftPlane(backgroundFetchPipeline.AttributePalette, 1, highByte: true),
                NextTileShifts: 0,
                HasNextTile: false);
            return;
        }

        backgroundShiftRegister = backgroundShiftRegister with
        {
            NextRenderAddress = backgroundFetchPipeline.RenderAddress,
            PatternLow = (ushort)((backgroundShiftRegister.PatternLow & 0xFF00) | backgroundFetchPipeline.PatternLow),
            PatternHigh = (ushort)((backgroundShiftRegister.PatternHigh & 0xFF00) | backgroundFetchPipeline.PatternHigh),
            AttributeLow = (ushort)((backgroundShiftRegister.AttributeLow & 0xFF00) |
                GetAttributeShiftPlane(backgroundFetchPipeline.AttributePalette, 0, highByte: false)),
            AttributeHigh = (ushort)((backgroundShiftRegister.AttributeHigh & 0xFF00) |
                GetAttributeShiftPlane(backgroundFetchPipeline.AttributePalette, 1, highByte: false)),
            NextTileShifts = 0,
            HasNextTile = true
        };
    }

    private static ushort GetAttributeShiftPlane(byte palette, int bit, bool highByte)
    {
        if (((palette >> bit) & 0x01) == 0)
        {
            return 0;
        }

        return highByte ? (ushort)0xFF00 : (ushort)0x00FF;
    }

    private void ShiftBackgroundRegisters()
    {
        if (!backgroundShiftRegister.IsValid ||
            Scanline is < 0 or >= ScreenHeight ||
            Dot is < 1 or > ScreenWidth)
        {
            return;
        }

        var shifted = backgroundShiftRegister with
        {
            PatternLow = (ushort)(backgroundShiftRegister.PatternLow << 1),
            PatternHigh = (ushort)(backgroundShiftRegister.PatternHigh << 1),
            AttributeLow = (ushort)(backgroundShiftRegister.AttributeLow << 1),
            AttributeHigh = (ushort)(backgroundShiftRegister.AttributeHigh << 1),
            NextTileShifts = backgroundShiftRegister.HasNextTile
                ? backgroundShiftRegister.NextTileShifts + 1
                : backgroundShiftRegister.NextTileShifts
        };

        backgroundShiftRegister = shifted.HasNextTile && shifted.NextTileShifts >= 8
            ? shifted with
            {
                RenderAddress = shifted.NextRenderAddress,
                NextRenderAddress = 0,
                NextTileShifts = 0,
                HasNextTile = false
            }
            : shifted;
    }

    public bool PollNmi()
    {
        if (!nmiPending)
        {
            return false;
        }

        if (nmiDelayDots > 0)
        {
            return false;
        }

        if (nmiPollDelay > 0)
        {
            nmiPollDelay--;
            return false;
        }

        nmiPending = false;
        return true;
    }

    public byte ReadPatternTable(ushort address) => cartridge.PpuRead(address);

    public void WritePatternTable(ushort address, byte value) => cartridge.PpuWrite(address, value);

    public void WriteOamDmaByte(byte value)
    {
        oam[oamAddress++] = value;
    }

    private bool IsNmiEnabled => (registers[0] & NmiEnableFlag) != 0;

    private bool IsVblankSet => (registers[2] & VblankFlag) != 0;

    private bool CanTriggerImmediateNmi => IsVblankSet && (Scanline != 261 || Dot != 0);

    private bool CanCancelVblankStartNmi => IsVblankSet && Scanline == 241 && Dot <= 2;

    private bool IsRenderingEnabled => (effectiveMask & 0x18) != 0;

    private bool IsBackgroundEnabled => (effectiveMask & 0x08) != 0;

    private bool IsSpriteRenderingEnabled => (effectiveMask & 0x10) != 0;

    private bool ShowBackgroundInLeftColumn => (effectiveMask & 0x02) != 0;

    private bool ShowSpritesInLeftColumn => (effectiveMask & 0x04) != 0;

    private bool IsOddFrame => (Frame & 1) != 0;

    private int VramIncrement => (registers[0] & 0x04) == 0 ? 1 : 32;

    private bool IsRenderingScanline => Scanline is >= 0 and <= 239 or 261;

    private void SetVblank()
    {
        registers[2] |= VblankFlag;
        vblankSetTotalDots = TotalDots;
        if (IsNmiEnabled)
        {
            nmiPending = true;
            nmiDelayDots = 2;
        }
    }

    private void ClearRenderingFlags()
    {
        registers[2] = (byte)(registers[2] & ~(VblankFlag | SpriteZeroHitFlag | SpriteOverflowFlag));
        nmiDelayDots = 0;
        nmiPollDelay = 0;
        suppressVblankSet = false;
    }

    private void ApplyStatusReadVblankSuppression()
    {
        if (Scanline == 241 && Dot == 0)
        {
            suppressVblankSet = true;
            nmiPending = false;
            nmiDelayDots = 0;
            nmiPollDelay = 0;
            NmiSuppressed?.Invoke();
            return;
        }

        if (IsVblankSet && TotalDots >= vblankSetTotalDots && TotalDots - vblankSetTotalDots <= 1)
        {
            nmiPending = false;
            nmiDelayDots = 0;
            nmiPollDelay = 0;
            NmiSuppressed?.Invoke();
        }
    }

    private void TickNmiDelay()
    {
        if (nmiDelayDots > 0)
        {
            nmiDelayDots--;
        }
    }

    private void TickMaskDelay()
    {
        if (pendingMaskDelayDots <= 0)
        {
            return;
        }

        pendingMaskDelayDots--;
        if (pendingMaskDelayDots == 0)
        {
            effectiveMask = pendingMask;
        }
    }

    private void UpdateRenderingVramAddress()
    {
        if (!IsRenderingEnabled || !IsRenderingScanline)
        {
            return;
        }

        if ((Dot is >= 1 and <= 256 || Dot is >= 321 and <= 336) && Dot % 8 == 0)
        {
            IncrementHorizontalVramAddress();
        }

        if (Dot == 256)
        {
            IncrementVerticalVramAddress();
        }
        else if (Dot == 257)
        {
            CopyHorizontalVramAddress();
        }
        else if (Scanline == 261 && Dot is >= 280 and <= 304)
        {
            CopyVerticalVramAddress();
        }
    }

    private void IncrementHorizontalVramAddress()
    {
        if ((currentVramAddress & 0x001F) == 31)
        {
            currentVramAddress = (ushort)((currentVramAddress & 0xFFE0) ^ 0x0400);
            return;
        }

        currentVramAddress++;
    }

    private void IncrementVerticalVramAddress()
    {
        if ((currentVramAddress & 0x7000) != 0x7000)
        {
            currentVramAddress += 0x1000;
            return;
        }

        currentVramAddress = (ushort)(currentVramAddress & 0x8FFF);
        var coarseY = (currentVramAddress & 0x03E0) >> 5;
        if (coarseY == 29)
        {
            coarseY = 0;
            currentVramAddress ^= 0x0800;
        }
        else if (coarseY == 31)
        {
            coarseY = 0;
        }
        else
        {
            coarseY++;
        }

        currentVramAddress = (ushort)((currentVramAddress & 0xFC1F) | (coarseY << 5));
    }

    private void CopyHorizontalVramAddress()
    {
        currentVramAddress = (ushort)((currentVramAddress & 0xFBE0) | (temporaryVramAddress & 0x041F));
    }

    private void CopyVerticalVramAddress()
    {
        currentVramAddress = (ushort)((currentVramAddress & 0x841F) | (temporaryVramAddress & 0x7BE0));
    }

    private byte ReadOamData()
    {
        var value = oam[oamAddress];
        value = (oamAddress & 0x03) == 0x02 ? (byte)(value & 0xE3) : value;
        SetOpenBus(value);
        return value;
    }

    private byte ReadPpuData()
    {
        var address = (ushort)(currentVramAddress & 0x3FFF);
        byte result;
        if (address >= 0x3F00)
        {
            result = (byte)((ReadMemory(address) & 0x3F) | (GetOpenBus() & 0xC0));
            readBuffer = ReadMemory((ushort)(address - 0x1000));
        }
        else
        {
            result = readBuffer;
            readBuffer = ReadMemory(address);
        }

        IncrementVramAddress();
        SetOpenBus(result);
        return result;
    }

    private void WritePpuData(byte value)
    {
        WriteMemory((ushort)(currentVramAddress & 0x3FFF), value);
        IncrementVramAddress();
    }

    private void WriteScroll(byte value)
    {
        if (!writeToggle)
        {
            fineX = (byte)(value & 0x07);
            scrollX = value;
            temporaryVramAddress = (ushort)((temporaryVramAddress & 0x7FE0) | (value >> 3));
            renderBackgroundFromCurrentVramAddress = false;
            backgroundShiftRegister = default;
            writeToggle = true;
            return;
        }

        scrollY = value;
        temporaryVramAddress = (ushort)((temporaryVramAddress & 0x0C1F) | ((value & 0x07) << 12) | ((value & 0xF8) << 2));
        renderBackgroundFromCurrentVramAddress = false;
        backgroundShiftRegister = default;
        writeToggle = false;
    }

    private void WriteAddress(byte value)
    {
        if (!writeToggle)
        {
            temporaryVramAddress = (ushort)((temporaryVramAddress & 0x00FF) | ((value & 0x3F) << 8));
            writeToggle = true;
            return;
        }

        temporaryVramAddress = (ushort)((temporaryVramAddress & 0x7F00) | value);
        currentVramAddress = temporaryVramAddress;
        cartridge.NotifyPpuAddress(currentVramAddress);
        renderBackgroundFromCurrentVramAddress = currentVramAddress < 0x2000;
        backgroundShiftRegister = default;
        if (renderBackgroundFromCurrentVramAddress && ShouldPrimeBackgroundShiftRegister())
        {
            PrimeBackgroundShiftRegister(currentVramAddress);
        }

        writeToggle = false;
    }

    private bool ShouldPrimeBackgroundShiftRegister()
    {
        return !IsRenderingEnabled ||
            !IsRenderingScanline ||
            Dot < 7;
    }

    private void PrimeBackgroundShiftRegister(ushort renderAddress)
    {
        var key = (ushort)(renderAddress & 0x7FFF);
        var tileIndex = FetchBackgroundNametableByte(key);
        var fineY = (key >> 12) & 0x07;
        var patternAddress = GetBackgroundPatternAddress(tileIndex, fineY);
        var attributePalette = FetchBackgroundAttributePalette(key);
        backgroundShiftRegister = new BackgroundShiftRegister(
            true,
            key,
            NextRenderAddress: 0,
            PatternLow: (ushort)(FetchBackgroundPatternLow(patternAddress) << 8),
            PatternHigh: (ushort)(FetchBackgroundPatternHigh(patternAddress) << 8),
            AttributeLow: GetAttributeShiftPlane(attributePalette, 0, highByte: true),
            AttributeHigh: GetAttributeShiftPlane(attributePalette, 1, highByte: true),
            NextTileShifts: 0,
            HasNextTile: false);
    }

    private void IncrementVramAddress()
    {
        currentVramAddress = (ushort)((currentVramAddress + VramIncrement) & 0x7FFF);
        cartridge.NotifyPpuAddress(currentVramAddress);
    }

    private byte ReadMemory(ushort address)
    {
        address = NormalizePpuAddress(address);
        return address switch
        {
            <= 0x1FFF => cartridge.PpuRead(address),
            >= 0x2000 and <= 0x2FFF => nametableRam[MapNametableAddress(address)],
            >= 0x3F00 and <= 0x3FFF => paletteRam[MapPaletteAddress(address)],
            _ => 0
        };
    }

    private void WriteMemory(ushort address, byte value)
    {
        address = NormalizePpuAddress(address);
        switch (address)
        {
            case <= 0x1FFF:
                cartridge.PpuWrite(address, value);
                break;
            case >= 0x2000 and <= 0x2FFF:
                nametableRam[MapNametableAddress(address)] = value;
                break;
            case >= 0x3F00 and <= 0x3FFF:
                paletteRam[MapPaletteAddress(address)] = value;
                break;
        }
    }

    private static ushort NormalizePpuAddress(ushort address)
    {
        address = (ushort)(address & 0x3FFF);
        if (address is >= 0x3000 and <= 0x3EFF)
        {
            address -= 0x1000;
        }

        return address;
    }

    private int MapNametableAddress(ushort address)
    {
        var offset = (address - 0x2000) & 0x0FFF;
        var table = offset / 0x0400;
        var inner = offset & 0x03FF;
        var physicalTable = cartridge.CurrentMirroringMode switch
        {
            MirroringMode.OneScreenLower => 0,
            MirroringMode.OneScreenUpper => 1,
            MirroringMode.Vertical => table & 0x01,
            MirroringMode.Horizontal => table >> 1,
            MirroringMode.FourScreen => table & 0x01,
            _ => table & 0x01
        };

        return physicalTable * 0x0400 + inner;
    }

    private static int MapPaletteAddress(ushort address)
    {
        var offset = (address - 0x3F00) & 0x001F;
        return offset switch
        {
            0x10 => 0x00,
            0x14 => 0x04,
            0x18 => 0x08,
            0x1C => 0x0C,
            _ => offset
        };
    }

    private byte GetOpenBus()
    {
        if (TotalDots - openBusLowUpdatedAt >= OpenBusDecayDots)
        {
            openBus = (byte)(openBus & 0xE0);
        }

        if (TotalDots - openBusHighUpdatedAt >= OpenBusDecayDots)
        {
            openBus = (byte)(openBus & 0x1F);
        }

        return openBus;
    }

    private void SetOpenBus(byte value, bool refreshLowBits = true)
    {
        openBus = refreshLowBits ? value : (byte)((openBus & 0x1F) | (value & 0xE0));
        openBusHighUpdatedAt = TotalDots;
        if (refreshLowBits)
        {
            openBusLowUpdatedAt = TotalDots;
        }
    }

    private void EvaluateSpriteZeroHit()
    {
        if ((registers[2] & SpriteZeroHitFlag) != 0 ||
            !IsBackgroundEnabled ||
            !IsSpriteRenderingEnabled ||
            Scanline is < 0 or > 239 ||
            Dot is < 1 or > 256)
        {
            return;
        }

        var x = Dot - 1;
        var y = Scanline;
        if (x == 255)
        {
            return;
        }

        if (x < 8 && (!ShowBackgroundInLeftColumn || !ShowSpritesInLeftColumn))
        {
            return;
        }

        if (GetBackgroundPixel(x, y) == 0 || GetSpriteZeroPixel(x, y) == 0)
        {
            return;
        }

        registers[2] |= SpriteZeroHitFlag;
    }

    private void EvaluateSpriteOverflow()
    {
        if ((registers[2] & SpriteOverflowFlag) != 0 ||
            !IsRenderingEnabled ||
            Scanline is < 0 or > 239 ||
            Dot is < 65 or > 256)
        {
            return;
        }

        if (Dot >= GetSpriteOverflowDot(Scanline))
        {
            registers[2] |= SpriteOverflowFlag;
        }
    }

    private void RenderCurrentPixel()
    {
        if (Scanline is < 0 or >= ScreenHeight || Dot is < 1 or > ScreenWidth)
        {
            return;
        }

        var x = Dot - 1;
        var y = Scanline;
        var backgroundPixel = GetBackgroundPixelWithPalette(x, y);
        var spritePixel = GetSpritePixelWithPalette(x, y);
        framebuffer[y * ScreenWidth + x] = ComposePixel(backgroundPixel, spritePixel);
    }

    private byte ComposePixel(PpuPixel backgroundPixel, PpuPixel spritePixel)
    {
        if (spritePixel.Color == 0)
        {
            return ReadPaletteEntry(backgroundPixel.PaletteAddress);
        }

        if (backgroundPixel.Color == 0)
        {
            return ReadPaletteEntry(spritePixel.PaletteAddress);
        }

        return ReadPaletteEntry(spritePixel.PriorityBehindBackground ? backgroundPixel.PaletteAddress : spritePixel.PaletteAddress);
    }

    private int GetBackgroundPixel(int x, int y)
    {
        return GetBackgroundPixelWithPalette(x, y).Color;
    }

    private PpuPixel GetBackgroundPixelWithPalette(int x, int y)
    {
        if (!IsBackgroundEnabled || (x < 8 && !ShowBackgroundInLeftColumn))
        {
            return PpuPixel.Transparent;
        }

        if (backgroundShiftRegister.IsValid &&
            (renderBackgroundFromCurrentVramAddress ||
                backgroundShiftRegister.RenderAddress == (currentVramAddress & 0x7FFF)))
        {
            return GetBackgroundPixelFromShiftRegister();
        }

        return renderBackgroundFromCurrentVramAddress
            ? PpuPixel.Transparent
            : GetBackgroundPixelFromScrollCoordinates(x, y);
    }

    private PpuPixel GetBackgroundPixelFromShiftRegister()
    {
        var bit = 15 - fineX;
        var color = ((backgroundShiftRegister.PatternLow >> bit) & 0x01) |
            (((backgroundShiftRegister.PatternHigh >> bit) & 0x01) << 1);
        if (color == 0)
        {
            return PpuPixel.Transparent;
        }

        var palette = ((backgroundShiftRegister.AttributeLow >> bit) & 0x01) |
            (((backgroundShiftRegister.AttributeHigh >> bit) & 0x01) << 1);
        return new PpuPixel(color, (ushort)(0x3F00 + palette * 4 + color), false);
    }

    private byte FetchBackgroundNametableByte(ushort renderAddress)
    {
        return ReadMemory((ushort)(0x2000 | (renderAddress & 0x0FFF)));
    }

    private byte FetchBackgroundAttributePalette(ushort renderAddress)
    {
        var coarseX = renderAddress & 0x001F;
        var coarseY = (renderAddress >> 5) & 0x001F;
        var table = (renderAddress >> 10) & 0x03;
        var attributeAddress = (ushort)(0x23C0 | (table << 10) | ((coarseY >> 2) << 3) | (coarseX >> 2));
        var attribute = ReadMemory(attributeAddress);
        var shift = ((coarseY & 0x02) << 1) | (coarseX & 0x02);
        return (byte)((attribute >> shift) & 0x03);
    }

    private ushort GetBackgroundPatternAddress(byte tileIndex, int fineY)
    {
        var patternBase = (registers[0] & 0x10) == 0 ? 0x0000 : 0x1000;
        return (ushort)(patternBase + tileIndex * 16 + fineY);
    }

    private byte FetchBackgroundPatternLow(ushort patternAddress)
    {
        return cartridge.PpuRead(patternAddress);
    }

    private byte FetchBackgroundPatternHigh(ushort patternAddress)
    {
        return cartridge.PpuRead((ushort)(patternAddress + 8));
    }

    private PpuPixel GetBackgroundPixelFromScrollCoordinates(int x, int y)
    {
        var scrolledX = (scrollX + x) & 0x01FF;
        var scrolledY = (scrollY + y) & 0x01FF;
        var nametableSelect = registers[0] & 0x03;
        var horizontalTable = (scrolledX / 256) & 0x01;
        var verticalTable = (scrolledY / 240) & 0x01;
        var table = nametableSelect ^ horizontalTable ^ (verticalTable << 1);
        var tileX = (scrolledX & 0xFF) / 8;
        var tileY = (scrolledY % 240) / 8;
        var fineY = scrolledY & 0x07;
        var tileIndex = ReadMemory((ushort)(0x2000 + table * 0x0400 + tileY * 32 + tileX));
        var patternBase = (registers[0] & 0x10) == 0 ? 0x0000 : 0x1000;
        var patternAddress = (ushort)(patternBase + tileIndex * 16 + fineY);
        var low = cartridge.PpuRead(patternAddress);
        var high = cartridge.PpuRead((ushort)(patternAddress + 8));
        var bit = 7 - (scrolledX & 0x07);
        var color = ((low >> bit) & 0x01) | (((high >> bit) & 0x01) << 1);
        if (color == 0)
        {
            return PpuPixel.Transparent;
        }

        var attributeAddress = (ushort)(0x2000 + table * 0x0400 + 0x03C0 + (tileY / 4) * 8 + (tileX / 4));
        var attribute = ReadMemory(attributeAddress);
        var shift = ((tileY & 0x02) << 1) | (tileX & 0x02);
        var palette = (attribute >> shift) & 0x03;
        return new PpuPixel(color, (ushort)(0x3F00 + palette * 4 + color), false);
    }

    private static ushort OffsetBackgroundAddress(ushort address, int coarseXOffset)
    {
        address = (ushort)(address & 0x7FFF);
        while (coarseXOffset-- > 0)
        {
            if ((address & 0x001F) == 31)
            {
                address = (ushort)((address & 0xFFE0) ^ 0x0400);
            }
            else
            {
                address++;
            }
        }

        return address;
    }

    private int GetSpriteZeroPixel(int x, int y)
    {
        EnsureSpriteScanlineLoaded(y);
        for (var i = 0; i < scanlineSpriteCount; i++)
        {
            if (scanlineSprites[i].SpriteIndex != 0)
            {
                continue;
            }

            return GetSpriteEntryColor(scanlineSprites[i], x);
        }

        return 0;
    }

    private PpuPixel GetSpritePixelWithPalette(int x, int y)
    {
        if (!IsRenderingEnabled || !IsSpriteRenderingEnabled)
        {
            return PpuPixel.Transparent;
        }

        if (x < 8 && !ShowSpritesInLeftColumn)
        {
            return PpuPixel.Transparent;
        }

        EnsureSpriteScanlineLoaded(y);
        var selectedPixel = PpuPixel.Transparent;
        for (var i = 0; i < scanlineSpriteCount; i++)
        {
            var pixel = GetSpritePixel(scanlineSprites[i], x);
            if (selectedPixel.Color == 0 && pixel.Color != 0)
            {
                selectedPixel = pixel;
            }
        }

        return selectedPixel;
    }

    private void EnsureSpriteScanlineLoaded(int y)
    {
        if (scanlineSpriteY != y)
        {
            LoadSpriteScanline(y);
        }
    }

    private void LoadSpriteScanline(int y)
    {
        scanlineSpriteY = y;
        scanlineSpriteCount = 0;

        if (!IsSpriteRenderingEnabled || y is < 0 or >= ScreenHeight)
        {
            return;
        }

        for (var spriteIndex = 0; spriteIndex < 64 && scanlineSpriteCount < scanlineSprites.Length; spriteIndex++)
        {
            if (!IsSpriteInRenderRange(spriteIndex, y))
            {
                continue;
            }

            scanlineSprites[scanlineSpriteCount++] = CreateSpriteRenderEntry(spriteIndex, y);
        }
    }

    private bool IsSpriteInRenderRange(int spriteIndex, int y)
    {
        var offset = spriteIndex * 4;
        var spriteY = oam[offset] + 1;
        var height = (registers[0] & 0x20) == 0 ? 8 : 16;
        return y >= spriteY && y < spriteY + height;
    }

    private int GetSpriteOverflowDot(int y)
    {
        var dot = 65;
        var spritesFound = 0;
        var spriteIndex = 0;

        while (dot <= 256 && spriteIndex < 64)
        {
            if (spritesFound < 8)
            {
                var spriteY = oam[spriteIndex * 4];
                dot += 2;
                if (IsSpriteYInEvaluationRange(spriteY, y))
                {
                    spritesFound++;
                    dot += 6;
                }

                spriteIndex++;
                continue;
            }

            var byteIndex = 0;
            while (dot <= 256 && spriteIndex < 64)
            {
                // Hardware keeps advancing both sprite and byte index after secondary OAM fills.
                var candidateY = oam[spriteIndex * 4 + byteIndex];
                dot += 2;
                if (IsSpriteYInEvaluationRange(candidateY, y))
                {
                    return dot;
                }

                spriteIndex++;
                byteIndex = (byteIndex + 1) & 0x03;
            }
        }

        return int.MaxValue;
    }

    private bool IsSpriteYInEvaluationRange(byte spriteY, int y)
    {
        var height = (registers[0] & 0x20) == 0 ? 8 : 16;
        return y >= spriteY && y < spriteY + height;
    }

    private SpriteRenderEntry CreateSpriteRenderEntry(int spriteIndex, int y)
    {
        var offset = spriteIndex * 4;
        var spriteY = oam[offset] + 1;
        var tileIndex = oam[offset + 1];
        var attributes = oam[offset + 2];
        var spriteX = oam[offset + 3];
        var height = (registers[0] & 0x20) == 0 ? 8 : 16;
        var relativeY = y - spriteY;

        if ((attributes & 0x80) != 0)
        {
            relativeY = height - 1 - relativeY;
        }

        int patternBase;
        int tile;
        if (height == 16)
        {
            patternBase = (tileIndex & 0x01) * 0x1000;
            tile = tileIndex & 0xFE;
            if (relativeY >= 8)
            {
                tile++;
                relativeY -= 8;
            }
        }
        else
        {
            patternBase = (registers[0] & 0x08) == 0 ? 0x0000 : 0x1000;
            tile = tileIndex;
        }

        var patternAddress = (ushort)(patternBase + tile * 16 + relativeY);
        var low = cartridge.PpuPeek(patternAddress);
        var high = cartridge.PpuPeek((ushort)(patternAddress + 8));
        return new SpriteRenderEntry(spriteIndex, (byte)spriteX, attributes, low, high);
    }

    private static int GetSpriteEntryColor(SpriteRenderEntry sprite, int x)
    {
        var relativeX = x - sprite.X;
        if (relativeX is < 0 or >= 8)
        {
            return 0;
        }

        if ((sprite.Attributes & 0x40) != 0)
        {
            relativeX = 7 - relativeX;
        }

        var bit = 7 - relativeX;
        return ((sprite.PatternLow >> bit) & 0x01) | (((sprite.PatternHigh >> bit) & 0x01) << 1);
    }

    private static PpuPixel GetSpritePixel(SpriteRenderEntry sprite, int x)
    {
        var color = GetSpriteEntryColor(sprite, x);
        if (color == 0)
        {
            return PpuPixel.Transparent;
        }

        var palette = sprite.Attributes & 0x03;
        var priorityBehindBackground = (sprite.Attributes & 0x20) != 0;
        return new PpuPixel(color, (ushort)(0x3F10 + palette * 4 + color), priorityBehindBackground);
    }

    private byte ReadPaletteEntry(ushort paletteAddress)
    {
        return (byte)(paletteRam[MapPaletteAddress(paletteAddress)] & 0x3F);
    }

    private readonly record struct PpuPixel(int Color, ushort PaletteAddress, bool PriorityBehindBackground)
    {
        public static readonly PpuPixel Transparent = new(0, 0x3F00, false);
    }

    private readonly record struct BackgroundFetchPipeline(
        bool IsValid,
        ushort RenderAddress,
        byte TileIndex,
        byte AttributePalette,
        ushort PatternAddress,
        byte PatternLow,
        byte PatternHigh);

    private readonly record struct BackgroundShiftRegister(
        bool IsValid,
        ushort RenderAddress,
        ushort NextRenderAddress,
        ushort PatternLow,
        ushort PatternHigh,
        ushort AttributeLow,
        ushort AttributeHigh,
        int NextTileShifts,
        bool HasNextTile);

    private readonly record struct SpriteRenderEntry(
        int SpriteIndex,
        byte X,
        byte Attributes,
        byte PatternLow,
        byte PatternHigh);
}
