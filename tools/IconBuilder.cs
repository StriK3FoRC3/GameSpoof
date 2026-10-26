using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;

static class IconBuilder
{
    static void Main()
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var frames = new List<byte[]>();
        foreach (int size in sizes) using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb)) {
            using (var graphics = Graphics.FromImage(bitmap)) IconArt.Draw(graphics, size);
            using (var stream = new MemoryStream()) { bitmap.Save(stream, ImageFormat.Png); frames.Add(stream.ToArray()); }
            if (size == 256) bitmap.Save("app-icon.png", ImageFormat.Png);
        }
        using (var writer = new BinaryWriter(File.Create("app.ico"))) {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++) {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
            }
            foreach (var frame in frames) writer.Write(frame);
        }
    }
}
