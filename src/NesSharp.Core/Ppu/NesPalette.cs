namespace NesSharp.Core.Ppu;

public static class NesPalette
{
    private static readonly NesColor[] Colors = CreateMameCompatibleNtscPalette();

    public static NesColor GetRgb(byte paletteIndex) => Colors[paletteIndex & 0x3F];

    private static NesColor[] CreateMameCompatibleNtscPalette()
    {
        const double tint = 0.22;
        const double hue = 287.0;
        const double kr = 0.2989;
        const double kb = 0.1145;
        const double ku = 2.029;
        const double kv = 1.140;

        double[,] brightness =
        {
            { 0.50, 0.75, 1.0, 1.0 },
            { 0.29, 0.45, 0.73, 0.9 },
            { 0.0, 0.24, 0.47, 0.77 }
        };

        var colors = new NesColor[64];
        for (var intensity = 0; intensity < 4; intensity++)
        {
            for (var colorNumber = 0; colorNumber < 16; colorNumber++)
            {
                double saturation;
                double radians;
                double y;

                switch (colorNumber)
                {
                    case 0:
                        saturation = 0.0;
                        radians = 0.0;
                        y = brightness[0, intensity];
                        break;
                    case 13:
                        saturation = 0.0;
                        radians = 0.0;
                        y = brightness[2, intensity];
                        break;
                    case 14:
                    case 15:
                        saturation = 0.0;
                        radians = 0.0;
                        y = 0.0;
                        break;
                    default:
                        saturation = tint;
                        radians = DegreesToRadians(colorNumber * 30.0 + hue);
                        y = brightness[1, intensity];
                        break;
                }

                var u = saturation * Math.Cos(radians);
                var v = saturation * Math.Sin(radians);
                var red = (y + kv * v) * 255.0;
                var green = (y - ((kb * ku * u) + (kr * kv * v)) / (1.0 - kb - kr)) * 255.0;
                var blue = (y + ku * u) * 255.0;

                colors[(intensity * 16) + colorNumber] = new NesColor(
                    RoundColor(red),
                    RoundColor(green),
                    RoundColor(blue));
            }
        }

        return colors;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static byte RoundColor(double value) => (byte)Math.Clamp(Math.Floor(value + 0.5), 0.0, 255.0);
}
