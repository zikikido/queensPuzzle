#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

namespace Kido.GifImporter.Editor
{
    internal sealed class GifFrameData
    {
        public int Width;
        public int Height;
        public byte[] Rgba;
        public float DelaySeconds;
        public int RawDelayCentiseconds;
    }

    internal sealed class GifAnimationData
    {
        public int Width;
        public int Height;
        public readonly List<GifFrameData> Frames = new List<GifFrameData>();
    }

    // Small, dependency-free GIF89a decoder for Unity Editor import use.
    internal static class GifDecoder
    {
        private sealed class Reader
        {
            private readonly byte[] _data;
            private int _p;
            public Reader(byte[] data) { _data = data ?? throw new ArgumentNullException(nameof(data)); }
            public int Position => _p;
            public bool End => _p >= _data.Length;
            public byte U8() { if (_p >= _data.Length) throw new EndOfStreamException(); return _data[_p++]; }
            public ushort U16() { int a = U8(), b = U8(); return (ushort)(a | (b << 8)); }
            public byte[] Bytes(int n) { if (_p + n > _data.Length) throw new EndOfStreamException(); var r = new byte[n]; Buffer.BlockCopy(_data, _p, r, 0, n); _p += n; return r; }
            public string Ascii(int n) => System.Text.Encoding.ASCII.GetString(Bytes(n));
            public void Skip(int n) { if (_p + n > _data.Length) throw new EndOfStreamException(); _p += n; }
        }

        private struct Gce
        {
            public int Disposal;
            public bool Transparent;
            public byte TransparentIndex;
            public int DelayCs;
        }

        public static GifAnimationData Decode(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("GIF path is empty.");
            return Decode(File.ReadAllBytes(filePath));
        }

        public static GifAnimationData Decode(byte[] bytes)
        {
            var r = new Reader(bytes);
            string signature = r.Ascii(6);
            if (signature != "GIF87a" && signature != "GIF89a") throw new InvalidDataException("Not a valid GIF87a/GIF89a file.");

            int screenW = r.U16();
            int screenH = r.U16();
            byte packed = r.U8();
            bool hasGlobal = (packed & 0x80) != 0;
            int globalSize = 1 << ((packed & 0x07) + 1);
            byte bgIndex = r.U8();
            r.U8(); // pixel aspect
            byte[] globalPalette = hasGlobal ? ReadPalette(r, globalSize) : null;

            var result = new GifAnimationData { Width = screenW, Height = screenH };
            var canvas = new byte[screenW * screenH * 4];
            if (globalPalette != null && bgIndex < globalPalette.Length / 3)
            {
                byte br = globalPalette[bgIndex * 3], bg = globalPalette[bgIndex * 3 + 1], bb = globalPalette[bgIndex * 3 + 2];
                for (int i = 0; i < screenW * screenH; i++) { int o = i * 4; canvas[o] = br; canvas[o + 1] = bg; canvas[o + 2] = bb; canvas[o + 3] = 0; }
            }

            Gce gce = default;
            byte[] previousCanvas = null;
            int previousDisposal = 0;
            int prevLeft = 0, prevTop = 0, prevW = 0, prevH = 0;

            while (!r.End)
            {
                byte introducer = r.U8();
                if (introducer == 0x3B) break; // trailer
                if (introducer == 0x21)
                {
                    byte label = r.U8();
                    if (label == 0xF9)
                    {
                        int blockSize = r.U8();
                        if (blockSize != 4) throw new InvalidDataException("Invalid graphic control extension.");
                        byte gp = r.U8();
                        int delay = r.U16();
                        byte trans = r.U8();
                        r.U8();
                        gce = new Gce
                        {
                            Disposal = (gp >> 2) & 0x07,
                            Transparent = (gp & 0x01) != 0,
                            TransparentIndex = trans,
                            DelayCs = delay
                        };
                    }
                    else
                    {
                        SkipSubBlocks(r);
                    }
                    continue;
                }
                if (introducer != 0x2C) throw new InvalidDataException($"Unexpected GIF block 0x{introducer:X2} at {r.Position - 1}.");

                ApplyDisposal(canvas, previousCanvas, previousDisposal, prevLeft, prevTop, prevW, prevH, screenW, screenH);

                int left = r.U16();
                int top = r.U16();
                int w = r.U16();
                int h = r.U16();
                byte ip = r.U8();
                bool localTable = (ip & 0x80) != 0;
                bool interlaced = (ip & 0x40) != 0;
                int localSize = 1 << ((ip & 0x07) + 1);
                byte[] palette = localTable ? ReadPalette(r, localSize) : globalPalette;
                if (palette == null) throw new InvalidDataException("GIF frame has no color table.");

                int lzwMin = r.U8();
                byte[] compressed = ReadSubBlocks(r);
                byte[] indices = LzwDecode(compressed, lzwMin, w * h);
                if (interlaced) indices = Deinterlace(indices, w, h);

                previousCanvas = gce.Disposal == 3 ? (byte[])canvas.Clone() : null;
                DrawFrame(canvas, indices, palette, left, top, w, h, screenW, screenH, gce.Transparent, gce.TransparentIndex);

                result.Frames.Add(new GifFrameData
                {
                    Width = screenW,
                    Height = screenH,
                    Rgba = (byte[])canvas.Clone(),
                    RawDelayCentiseconds = gce.DelayCs,
                    DelaySeconds = gce.DelayCs / 100f
                });

                previousDisposal = gce.Disposal;
                prevLeft = left; prevTop = top; prevW = w; prevH = h;
                gce = default;
            }

            if (result.Frames.Count == 0) throw new InvalidDataException("GIF contains no image frames.");
            return result;
        }

        private static byte[] ReadPalette(Reader r, int count) => r.Bytes(count * 3);

        private static void SkipSubBlocks(Reader r)
        {
            while (true) { int n = r.U8(); if (n == 0) return; r.Skip(n); }
        }

        private static byte[] ReadSubBlocks(Reader r)
        {
            using (var ms = new MemoryStream())
            {
                while (true)
                {
                    int n = r.U8();
                    if (n == 0) break;
                    byte[] b = r.Bytes(n);
                    ms.Write(b, 0, b.Length);
                }
                return ms.ToArray();
            }
        }

        private static byte[] LzwDecode(byte[] data, int minCodeSize, int expected)
        {
            int clear = 1 << minCodeSize;
            int end = clear + 1;
            int codeSize = minCodeSize + 1;
            int nextCode = end + 1;
            var prefix = new int[4096];
            var suffix = new byte[4096];
            var stack = new byte[4097];
            for (int i = 0; i < clear; i++) { prefix[i] = -1; suffix[i] = (byte)i; }

            var output = new byte[expected];
            int outPos = 0, bitPos = 0, oldCode = -1;
            byte first = 0;

            while (true)
            {
                int code = ReadBits(data, ref bitPos, codeSize);
                if (code < 0 || code == end) break;
                if (code == clear)
                {
                    codeSize = minCodeSize + 1;
                    nextCode = end + 1;
                    oldCode = -1;
                    continue;
                }

                int inCode = code;
                int sp = 0;
                if (code >= nextCode)
                {
                    if (oldCode < 0) throw new InvalidDataException("Invalid GIF LZW stream.");
                    stack[sp++] = first;
                    code = oldCode;
                }

                while (code >= clear)
                {
                    if (code >= 4096 || sp >= stack.Length) throw new InvalidDataException("Invalid GIF LZW dictionary.");
                    stack[sp++] = suffix[code];
                    code = prefix[code];
                }
                first = suffix[code];
                stack[sp++] = first;

                while (sp > 0 && outPos < expected) output[outPos++] = stack[--sp];
                if (oldCode >= 0 && nextCode < 4096)
                {
                    prefix[nextCode] = oldCode;
                    suffix[nextCode] = first;
                    nextCode++;
                    if (nextCode == (1 << codeSize) && codeSize < 12) codeSize++;
                }
                oldCode = inCode;
                if (outPos >= expected) break;
            }
            return output;
        }

        private static int ReadBits(byte[] data, ref int bitPos, int count)
        {
            if (bitPos + count > data.Length * 8) return -1;
            int value = 0;
            for (int i = 0; i < count; i++)
            {
                int p = bitPos + i;
                value |= ((data[p >> 3] >> (p & 7)) & 1) << i;
            }
            bitPos += count;
            return value;
        }

        private static byte[] Deinterlace(byte[] src, int w, int h)
        {
            var dst = new byte[src.Length];
            int s = 0;
            int[] starts = { 0, 4, 2, 1 };
            int[] steps = { 8, 8, 4, 2 };
            for (int pass = 0; pass < 4; pass++)
                for (int y = starts[pass]; y < h; y += steps[pass])
                {
                    int n = Math.Min(w, src.Length - s);
                    if (n <= 0) return dst;
                    Buffer.BlockCopy(src, s, dst, y * w, n);
                    s += n;
                }
            return dst;
        }

        private static void ApplyDisposal(byte[] canvas, byte[] saved, int disposal, int left, int top, int w, int h, int sw, int sh)
        {
            if (disposal == 2)
            {
                for (int y = Math.Max(0, top); y < Math.Min(sh, top + h); y++)
                    for (int x = Math.Max(0, left); x < Math.Min(sw, left + w); x++)
                    {
                        int o = (y * sw + x) * 4;
                        canvas[o] = canvas[o + 1] = canvas[o + 2] = canvas[o + 3] = 0;
                    }
            }
            else if (disposal == 3 && saved != null && saved.Length == canvas.Length)
            {
                Buffer.BlockCopy(saved, 0, canvas, 0, canvas.Length);
            }
        }

        private static void DrawFrame(byte[] canvas, byte[] idx, byte[] pal, int left, int top, int w, int h, int sw, int sh, bool transparent, byte transparentIndex)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int si = y * w + x;
                    if (si >= idx.Length) return;
                    byte ci = idx[si];
                    if (transparent && ci == transparentIndex) continue;
                    int dx = left + x, dy = top + y;
                    if (dx < 0 || dy < 0 || dx >= sw || dy >= sh) continue;
                    int pi = ci * 3;
                    if (pi + 2 >= pal.Length) continue;
                    int o = (dy * sw + dx) * 4;
                    canvas[o] = pal[pi]; canvas[o + 1] = pal[pi + 1]; canvas[o + 2] = pal[pi + 2]; canvas[o + 3] = 255;
                }
        }
    }
}
#endif
