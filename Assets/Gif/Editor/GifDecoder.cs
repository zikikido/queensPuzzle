#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Kido.SpriteTimeline.Editor
{
    internal sealed class DecodedGif
    {
        internal int Width;
        internal int Height;
        internal readonly List<DecodedGifFrame> Frames = new List<DecodedGifFrame>();
    }

    internal sealed class DecodedGifFrame
    {
        internal Color32[] Pixels;
        internal float Duration;
    }

    internal static class GifDecoder
    {
        private sealed class Reader
        {
            private readonly byte[] data;
            private int p;
            internal Reader(byte[] bytes) { data = bytes; }
            internal bool End => p >= data.Length;
            internal byte Byte() => p < data.Length ? data[p++] : throw new EndOfStreamException();
            internal ushort U16() => (ushort)(Byte() | (Byte() << 8));
            internal byte[] Bytes(int n)
            {
                if (p + n > data.Length) throw new EndOfStreamException();
                var r = new byte[n]; Buffer.BlockCopy(data, p, r, 0, n); p += n; return r;
            }
            internal byte[] SubBlocks()
            {
                using (var ms = new MemoryStream())
                {
                    while (true)
                    {
                        int n = Byte();
                        if (n == 0) break;
                        var b = Bytes(n); ms.Write(b, 0, b.Length);
                    }
                    return ms.ToArray();
                }
            }
            internal void SkipSubBlocks() { while (true) { int n = Byte(); if (n == 0) return; Bytes(n); } }
        }

        private struct Gce
        {
            internal int Disposal;
            internal bool Transparent;
            internal byte TransparentIndex;
            internal float Duration;
        }

        private struct PrevFrame
        {
            internal int Disposal, X, Y, W, H;
            internal Color32[] Restore;
        }

        internal static DecodedGif Decode(string filePath)
        {
            var r = new Reader(File.ReadAllBytes(filePath));
            string sig = System.Text.Encoding.ASCII.GetString(r.Bytes(6));
            if (sig != "GIF87a" && sig != "GIF89a") throw new InvalidDataException("Not a valid GIF file.");

            int width = r.U16();
            int height = r.U16();
            byte packed = r.Byte();
            r.Byte(); // background index
            r.Byte(); // pixel aspect ratio
            Color32[] globalPalette = null;
            if ((packed & 0x80) != 0) globalPalette = ReadPalette(r, 1 << ((packed & 7) + 1));

            var result = new DecodedGif { Width = width, Height = height };
            var canvas = new Color32[width * height];
            var gce = new Gce { Duration = 0f };
            PrevFrame prev = default;
            bool hasPrev = false;

            while (!r.End)
            {
                byte marker = r.Byte();
                if (marker == 0x3B) break;
                if (marker == 0x21)
                {
                    byte label = r.Byte();
                    if (label == 0xF9)
                    {
                        int blockSize = r.Byte();
                        if (blockSize != 4) throw new InvalidDataException("Invalid GIF graphic control extension.");
                        byte gp = r.Byte();
                        ushort delayCs = r.U16();
                        byte transparentIndex = r.Byte();
                        r.Byte();
                        gce = new Gce
                        {
                            Disposal = (gp >> 2) & 7,
                            Transparent = (gp & 1) != 0,
                            TransparentIndex = transparentIndex,
                            Duration = delayCs / 100f
                        };
                    }
                    else
                    {
                        r.SkipSubBlocks();
                    }
                    continue;
                }

                if (marker != 0x2C) throw new InvalidDataException($"Unexpected GIF block 0x{marker:X2}.");

                if (hasPrev) ApplyDisposal(canvas, width, height, prev);

                int x = r.U16();
                int y = r.U16();
                int w = r.U16();
                int h = r.U16();
                byte ip = r.Byte();
                bool localTable = (ip & 0x80) != 0;
                bool interlaced = (ip & 0x40) != 0;
                Color32[] palette = localTable ? ReadPalette(r, 1 << ((ip & 7) + 1)) : globalPalette;
                if (palette == null) throw new InvalidDataException("GIF has no color table.");

                int minCodeSize = r.Byte();
                byte[] compressed = r.SubBlocks();
                byte[] indices = DecodeLzw(compressed, minCodeSize, w * h);

                Color32[] restore = gce.Disposal == 3 ? (Color32[])canvas.Clone() : null;
                DrawImage(canvas, width, height, x, y, w, h, palette, indices, interlaced, gce.Transparent, gce.TransparentIndex);

                result.Frames.Add(new DecodedGifFrame
                {
                    Pixels = (Color32[])canvas.Clone(),
                    Duration = gce.Duration
                });

                prev = new PrevFrame { Disposal = gce.Disposal, X = x, Y = y, W = w, H = h, Restore = restore };
                hasPrev = true;
                gce = new Gce { Duration = 0f };
            }

            if (result.Frames.Count == 0) throw new InvalidDataException("The GIF contains no frames.");
            return result;
        }

        private static Color32[] ReadPalette(Reader r, int count)
        {
            var p = new Color32[count];
            for (int i = 0; i < count; i++) p[i] = new Color32(r.Byte(), r.Byte(), r.Byte(), 255);
            return p;
        }

        private static void ApplyDisposal(Color32[] canvas, int width, int height, PrevFrame p)
        {
            if (p.Disposal == 2)
            {
                int maxY = Math.Min(height, p.Y + p.H), maxX = Math.Min(width, p.X + p.W);
                for (int yy = Math.Max(0, p.Y); yy < maxY; yy++)
                    for (int xx = Math.Max(0, p.X); xx < maxX; xx++)
                        canvas[yy * width + xx] = new Color32(0, 0, 0, 0);
            }
            else if (p.Disposal == 3 && p.Restore != null)
            {
                Buffer.BlockCopy(p.Restore, 0, canvas, 0, canvas.Length * 4);
            }
        }

        private static void DrawImage(Color32[] canvas, int cw, int ch, int x, int y, int w, int h,
            Color32[] palette, byte[] indices, bool interlaced, bool transparent, byte transparentIndex)
        {
            int src = 0;
            if (!interlaced)
            {
                for (int row = 0; row < h; row++) DrawRow(row);
            }
            else
            {
                int[] starts = { 0, 4, 2, 1 }, steps = { 8, 8, 4, 2 };
                for (int pass = 0; pass < 4; pass++)
                    for (int row = starts[pass]; row < h; row += steps[pass]) DrawRow(row);
            }

            void DrawRow(int row)
            {
                int dy = y + row;
                for (int col = 0; col < w && src < indices.Length; col++, src++)
                {
                    int dx = x + col;
                    byte idx = indices[src];
                    if (dx < 0 || dx >= cw || dy < 0 || dy >= ch) continue;
                    if (transparent && idx == transparentIndex) continue;
                    if (idx < palette.Length) canvas[dy * cw + dx] = palette[idx];
                }
            }
        }

        private static byte[] DecodeLzw(byte[] data, int minCodeSize, int expected)
        {
            int clear = 1 << minCodeSize;
            int end = clear + 1;
            int available = clear + 2;
            int oldCode = -1;
            int codeSize = minCodeSize + 1;
            int codeMask = (1 << codeSize) - 1;
            var prefix = new short[4096];
            var suffix = new byte[4096];
            var stack = new byte[4097];
            for (int i = 0; i < clear; i++) suffix[i] = (byte)i;

            var output = new byte[expected];
            int outPos = 0, datum = 0, bits = 0, dataPos = 0, top = 0, first = 0;

            while (outPos < expected)
            {
                if (top == 0)
                {
                    while (bits < codeSize)
                    {
                        if (dataPos >= data.Length) return output;
                        datum |= data[dataPos++] << bits;
                        bits += 8;
                    }
                    int code = datum & codeMask;
                    datum >>= codeSize;
                    bits -= codeSize;

                    if (code == clear)
                    {
                        codeSize = minCodeSize + 1;
                        codeMask = (1 << codeSize) - 1;
                        available = clear + 2;
                        oldCode = -1;
                        continue;
                    }
                    if (code == end) break;
                    if (oldCode == -1)
                    {
                        output[outPos++] = suffix[code];
                        first = code;
                        oldCode = code;
                        continue;
                    }

                    int inCode = code;
                    if (code >= available)
                    {
                        stack[top++] = (byte)first;
                        code = oldCode;
                    }
                    while (code >= clear)
                    {
                        stack[top++] = suffix[code];
                        code = prefix[code];
                    }
                    first = suffix[code];
                    stack[top++] = (byte)first;

                    if (available < 4096)
                    {
                        prefix[available] = (short)oldCode;
                        suffix[available] = (byte)first;
                        available++;
                        if ((available & codeMask) == 0 && available < 4096)
                        {
                            codeSize++;
                            codeMask = (1 << codeSize) - 1;
                        }
                    }
                    oldCode = inCode;
                }
                top--;
                output[outPos++] = stack[top];
            }
            return output;
        }
    }
}
#endif
