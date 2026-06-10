using NesSharp.Core.Cartridge;
using NesSharp.Core.Input;
using NesSharp.Core.Memory;

namespace NesSharp.Tests;

public sealed class ControllerTests
{
    [Fact]
    public void StandardControllerReadsLatchedButtonsInNesOrder()
    {
        var controller = new StandardController
        {
            State = ControllerButton.A | ControllerButton.Start | ControllerButton.Left
        };

        controller.WriteStrobe(true);
        controller.WriteStrobe(false);

        var bits = Enumerable.Range(0, 10).Select(_ => controller.Read() & 0x01).ToArray();

        Assert.Equal([1, 0, 0, 1, 0, 0, 1, 0, 1, 1], bits);
    }

    [Fact]
    public void StrobeHighContinuouslyReadsCurrentAButton()
    {
        var controller = new StandardController { State = ControllerButton.A };

        controller.WriteStrobe(true);

        Assert.Equal(1, controller.Read() & 0x01);

        controller.State = 0;

        Assert.Equal(0, controller.Read() & 0x01);
    }

    [Fact]
    public void CpuBusExposesControllerThrough4016()
    {
        var bus = new CpuBus(CreateCartridge());
        bus.Controller1.State = ControllerButton.B | ControllerButton.Right;

        bus.Write(0x4016, 1);
        bus.Write(0x4016, 0);

        var bits = Enumerable.Range(0, 8).Select(_ => bus.Read(0x4016) & 0x01).ToArray();

        Assert.Equal([0, 1, 0, 0, 0, 0, 0, 1], bits);
    }

    [Fact]
    public void DmcDmaDuringControllerReadDeletesOneControllerBit()
    {
        var bus = new CpuBus(CreateCartridge());
        bus.Controller1.State = ControllerButton.A | ControllerButton.Select | ControllerButton.Down;
        bus.Write(0x4016, 1);
        bus.Write(0x4016, 0);
        bus.WriteRaw(0x4013, 0x00);
        bus.WriteRaw(0x4015, 0x10);

        var first = bus.Read(0x4016) & 0x01;
        var second = bus.Read(0x4016) & 0x01;

        Assert.Equal(1, first);
        Assert.Equal(1, second);
    }

    private static Cartridge CreateCartridge()
    {
        var rom = new byte[16 + 16 * 1024 + 8 * 1024];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = 1;
        rom[5] = 1;
        return INesRomLoader.Load(rom);
    }
}
