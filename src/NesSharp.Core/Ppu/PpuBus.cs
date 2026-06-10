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
    private bool nmiPending;
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
                if (!wasNmiEnabled && IsNmiEnabled && IsVblankSet)
                {
                    nmiPending = true;
                    nmiPollDelay = 1;
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
        Dot = 0;
        Scanline = 0;
        Frame = 0;
        TotalDots = 0;
        nmiPending = false;
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
        vblankSetTotalDots = 0;
    }

    public void Clock(int ppuCycles)
    {
        for (var i = 0; i < ppuCycles; i++)
        {
            if (Scanline == 261 && Dot == 339 && IsOddFrame && IsRenderingEnabled)
            {
                TotalDots++;
                Dot = 0;
                Scanline = 0;
                Frame++;
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

            RenderCurrentPixel();
            EvaluateSpriteZeroHit();
        }
    }

    public bool PollNmi()
    {
        if (!nmiPending)
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

    private bool IsRenderingEnabled => (registers[1] & 0x18) != 0;

    private bool IsBackgroundEnabled => (registers[1] & 0x08) != 0;

    private bool IsSpriteRenderingEnabled => (registers[1] & 0x10) != 0;

    private bool ShowBackgroundInLeftColumn => (registers[1] & 0x02) != 0;

    private bool ShowSpritesInLeftColumn => (registers[1] & 0x04) != 0;

    private bool IsOddFrame => (Frame & 1) != 0;

    private int VramIncrement => (registers[0] & 0x04) == 0 ? 1 : 32;

    private void SetVblank()
    {
        registers[2] |= VblankFlag;
        vblankSetTotalDots = TotalDots;
        if (IsNmiEnabled)
        {
            nmiPending = true;
        }
    }

    private void ClearRenderingFlags()
    {
        registers[2] = (byte)(registers[2] & ~(VblankFlag | SpriteZeroHitFlag | SpriteOverflowFlag));
        nmiPending = false;
        nmiPollDelay = 0;
        suppressVblankSet = false;
    }

    private void ApplyStatusReadVblankSuppression()
    {
        if (Scanline == 241 && Dot == 0)
        {
            suppressVblankSet = true;
            nmiPending = false;
            nmiPollDelay = 0;
            NmiSuppressed?.Invoke();
            return;
        }

        if (IsVblankSet && TotalDots >= vblankSetTotalDots && TotalDots - vblankSetTotalDots <= 1)
        {
            nmiPending = false;
            nmiPollDelay = 0;
            NmiSuppressed?.Invoke();
        }
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
            writeToggle = true;
            return;
        }

        scrollY = value;
        temporaryVramAddress = (ushort)((temporaryVramAddress & 0x0C1F) | ((value & 0x07) << 12) | ((value & 0xF8) << 2));
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
        writeToggle = false;
    }

    private void IncrementVramAddress()
    {
        currentVramAddress = (ushort)((currentVramAddress + VramIncrement) & 0x7FFF);
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

    private int GetSpriteZeroPixel(int x, int y)
    {
        var spriteY = oam[0] + 1;
        var tileIndex = oam[1];
        var attributes = oam[2];
        var spriteX = oam[3];
        var height = (registers[0] & 0x20) == 0 ? 8 : 16;
        var relativeX = x - spriteX;
        var relativeY = y - spriteY;

        if (relativeX is < 0 or >= 8 || relativeY < 0 || relativeY >= height)
        {
            return 0;
        }

        if ((attributes & 0x40) != 0)
        {
            relativeX = 7 - relativeX;
        }

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
        var low = cartridge.PpuRead(patternAddress);
        var high = cartridge.PpuRead((ushort)(patternAddress + 8));
        var bit = 7 - relativeX;
        return ((low >> bit) & 0x01) | (((high >> bit) & 0x01) << 1);
    }

    private PpuPixel GetSpritePixelWithPalette(int x, int y)
    {
        if (!IsRenderingEnabled)
        {
            return PpuPixel.Transparent;
        }

        var canRenderSpritePixels = IsSpriteRenderingEnabled && (x >= 8 || ShowSpritesInLeftColumn);
        var spritesOnScanline = 0;
        var renderSpritesOnScanline = 0;
        var selectedPixel = PpuPixel.Transparent;
        for (var spriteIndex = 0; spriteIndex < 64; spriteIndex++)
        {
            if (IsSpriteInEvaluationRange(spriteIndex, y))
            {
                spritesOnScanline++;
                if (spritesOnScanline > 8)
                {
                    registers[2] |= SpriteOverflowFlag;
                }
            }

            if (!canRenderSpritePixels || !IsSpriteInRenderRange(spriteIndex, y))
            {
                continue;
            }

            renderSpritesOnScanline++;
            if (renderSpritesOnScanline > 8)
            {
                continue;
            }

            var pixel = GetSpritePixel(spriteIndex, x, y);
            if (selectedPixel.Color == 0 && pixel.Color != 0)
            {
                selectedPixel = pixel;
            }
        }

        return selectedPixel;
    }

    private bool IsSpriteInRenderRange(int spriteIndex, int y)
    {
        var offset = spriteIndex * 4;
        var spriteY = oam[offset] + 1;
        var height = (registers[0] & 0x20) == 0 ? 8 : 16;
        return y >= spriteY && y < spriteY + height;
    }

    private bool IsSpriteInEvaluationRange(int spriteIndex, int y)
    {
        var offset = spriteIndex * 4;
        var spriteY = oam[offset];
        var height = (registers[0] & 0x20) == 0 ? 8 : 16;
        return y >= spriteY && y < spriteY + height;
    }

    private PpuPixel GetSpritePixel(int spriteIndex, int x, int y)
    {
        var offset = spriteIndex * 4;
        var spriteY = oam[offset] + 1;
        var tileIndex = oam[offset + 1];
        var attributes = oam[offset + 2];
        var spriteX = oam[offset + 3];
        var height = (registers[0] & 0x20) == 0 ? 8 : 16;
        var relativeX = x - spriteX;
        var relativeY = y - spriteY;

        if (relativeX is < 0 or >= 8 || relativeY < 0 || relativeY >= height)
        {
            return PpuPixel.Transparent;
        }

        if ((attributes & 0x40) != 0)
        {
            relativeX = 7 - relativeX;
        }

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
        var low = cartridge.PpuRead(patternAddress);
        var high = cartridge.PpuRead((ushort)(patternAddress + 8));
        var bit = 7 - relativeX;
        var color = ((low >> bit) & 0x01) | (((high >> bit) & 0x01) << 1);
        if (color == 0)
        {
            return PpuPixel.Transparent;
        }

        var palette = attributes & 0x03;
        var priorityBehindBackground = (attributes & 0x20) != 0;
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
}
