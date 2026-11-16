#Requires -Version 5.1
<#
Draws the CursorMeter logo: a rounded island with the two quota bars.
Writes assets\CursorMeter.ico (16 to 256 px) and assets\logo.png (512 px).
assets\logo.svg and scripts\linux\cursormeter.svg hold the same geometry; keep them in step.
#>
Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root "assets"
New-Item -ItemType Directory -Force $assets | Out-Null

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class LogoMaker
{
    // Geometry on a 256 grid, as in logo.svg.
    const float Radius = 60, Inset = 6;
    const float BarLeft = 44, BarWidth = 168, BarHeight = 44, BarRadius = 14;
    const float Row1 = 72, Row2 = 140;

    static GraphicsPath Round(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.ScaleTransform(size / 256f, size / 256f);

            using (var path = Round(Inset, Inset, 256 - 2 * Inset, 256 - 2 * Inset, Radius))
            using (var fill = new SolidBrush(Color.FromArgb(0x1C, 0x1C, 0x1C)))
            using (var pen = new Pen(Color.FromArgb(0x5E, 0x81, 0xAC), 8))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            Bar(g, Row1, 0.42f, Color.FromArgb(0x7D, 0xCE, 0xA0));
            Bar(g, Row2, 0.78f, Color.FromArgb(0xE0, 0xB1, 0x5A));
        }
        return bmp;
    }

    static void Bar(Graphics g, float y, float share, Color color)
    {
        using (var track = Round(BarLeft, y, BarWidth, BarHeight, BarRadius))
        using (var trackBrush = new SolidBrush(Color.FromArgb(0x3A, 0x3A, 0x3A)))
            g.FillPath(trackBrush, track);
        using (var value = Round(BarLeft, y, BarWidth * share, BarHeight, BarRadius))
        using (var valueBrush = new SolidBrush(color))
            g.FillPath(valueBrush, value);
    }

    public static void WriteIco(string path, int[] sizes)
    {
        var images = new List<byte[]>();
        foreach (int size in sizes)
        {
            using (var bmp = Draw(size))
                images.Add(size >= 256 ? Png(bmp) : Dib(bmp));
        }

        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var image in images) w.Write(image);
        }
    }

    static byte[] Png(Bitmap bmp)
    {
        using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    // BITMAPINFOHEADER, bottom-up BGRA pixels, then a 1-bit AND mask that stays zero (alpha decides).
    static byte[] Dib(Bitmap bmp)
    {
        int s = bmp.Width;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(40); w.Write(s); w.Write(s * 2); w.Write((ushort)1); w.Write((ushort)32);
            w.Write(0); w.Write(s * s * 4); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = s - 1; y >= 0; y--)
                for (int x = 0; x < s; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
                }
            int maskRow = ((s + 31) / 32) * 4;
            w.Write(new byte[maskRow * s]);
            return ms.ToArray();
        }
    }
}
'@

[LogoMaker]::WriteIco((Join-Path $assets "CursorMeter.ico"), @(16, 20, 24, 32, 40, 48, 64, 128, 256))
$png = [LogoMaker]::Draw(512)
$png.Save((Join-Path $assets "logo.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$png.Dispose()
Write-Host "Wrote assets\CursorMeter.ico and assets\logo.png"
