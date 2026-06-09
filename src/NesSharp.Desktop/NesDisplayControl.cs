using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using NesSharp.Core.Ppu;

namespace NesSharp.Desktop;

internal sealed class NesDisplayControl : Control
{
    public const int NesWidth = 256;
    public const int NesHeight = 240;

    private readonly Bitmap bitmap = new(NesWidth, NesHeight, PixelFormat.Format32bppArgb);
    private readonly byte[] bitmapBytes = new byte[NesWidth * NesHeight * 4];

    public NesDisplayControl()
    {
        DoubleBuffered = true;
        BackColor = Color.Black;
        MinimumSize = new Size(NesWidth, NesHeight);
    }

    public void UpdateFrame(ReadOnlySpan<byte> framebuffer)
    {
        if (framebuffer.Length < NesWidth * NesHeight)
        {
            throw new ArgumentException("Framebuffer is smaller than the NES visible screen.", nameof(framebuffer));
        }

        for (var i = 0; i < NesWidth * NesHeight; i++)
        {
            var color = NesPalette.GetRgb(framebuffer[i]);
            var offset = i * 4;
            bitmapBytes[offset] = color.B;
            bitmapBytes[offset + 1] = color.G;
            bitmapBytes[offset + 2] = color.R;
            bitmapBytes[offset + 3] = 0xFF;
        }

        var bounds = new Rectangle(0, 0, NesWidth, NesHeight);
        var data = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(bitmapBytes, 0, data.Scan0, bitmapBytes.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(Color.Black);
        e.Graphics.CompositingMode = CompositingMode.SourceCopy;
        e.Graphics.CompositingQuality = CompositingQuality.HighSpeed;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.SmoothingMode = SmoothingMode.None;
        e.Graphics.DrawImage(bitmap, GetDestinationRectangle());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            bitmap.Dispose();
        }

        base.Dispose(disposing);
    }

    private Rectangle GetDestinationRectangle()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return Rectangle.Empty;
        }

        var scale = Math.Min(ClientSize.Width / (double)NesWidth, ClientSize.Height / (double)NesHeight);
        var width = Math.Max(1, (int)Math.Floor(NesWidth * scale));
        var height = Math.Max(1, (int)Math.Floor(NesHeight * scale));
        var x = (ClientSize.Width - width) / 2;
        var y = (ClientSize.Height - height) / 2;
        return new Rectangle(x, y, width, height);
    }
}
