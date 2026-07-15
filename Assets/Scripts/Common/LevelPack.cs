using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace QueensPuzzle
{
    /// <summary>
    /// The shipped level container: every exported level packed into ONE encrypted binary file
    /// (Resources/Levels/levels.bytes) instead of thousands of ScriptableObjects.
    ///
    /// Plain layout (before encryption):
    ///   "QPLV" (4B) - version (1B) - levelSetId (string) - count (int32)
    ///   lookup table: count x int32 - byte offset of each level from the payload start
    ///   payload, per level: size (1B) - weight (uint16) - revealedRows bitmask (uint16)
    ///     - regions (size^2 bytes) - solutionColumns (size bytes) - regionColors (size bytes)

    /// </summary>
    public static class LevelPack
    {
        const byte Version = 2;
        static readonly byte[] MagicBytes = { (byte)'Q', (byte)'P', (byte)'L', (byte)'V' };

        /// <summary>
        /// One level's content - the RUNTIME level type. The LevelData ScriptableObject is the
        /// editor-side authoring format only; the game plays these, decoded from the pack (or
        /// converted from an asset for editor playtests).
        /// </summary>
        public sealed class Level
        {
            public int size;
            public int weight;
            public int[] regions;          // size^2 region ids
            public int[] solutionColumns;  // size entries
            public int[] revealedRows;     // may be null
            public int[] regionColors;     // may be null -> identity is written on encode

            /// <summary>Region id at the given cell.</summary>
            public int RegionAt(int row, int col) => regions[row * size + col];

            /// <summary>True if the queen in the solution sits at this cell.</summary>
            public bool IsSolutionQueen(int row, int col) => solutionColumns[row] == col;

            /// <summary>True if this row's solution queen starts revealed on the board.</summary>
            public bool IsRevealedRow(int row) => revealedRows != null && Array.IndexOf(revealedRows, row) >= 0;

            /// <summary>Palette (SORegionsColors) index a region is shown with - authored levels
            /// pick their colours; null falls back to "region k = colour k" (editor playtests).</summary>
            public int ColorOf(int region) =>
                regionColors != null && region >= 0 && region < regionColors.Length ? regionColors[region] : region;

            /// <summary>Stable hash of the PLAYABLE content (size + regions + solution). A saved
            /// board may only be restored onto the exact puzzle it was played on - if a level is
            /// redesigned (even at the same size), the hash changes and the stale save is rejected.
            /// Must stay byte-identical to the historical LevelData hash - players' saves carry it.</summary>
            public int ContentHash()
            {
                unchecked
                {
                    int h = 17;
                    h = h * 31 + size;
                    if (regions != null) foreach (int r in regions) h = h * 31 + r;
                    if (solutionColumns != null) foreach (int c in solutionColumns) h = h * 31 + c;
                    return h;
                }
            }
        }

        // ---- encode (editor / tests) ---------------------------------------------------

        /// <summary>Builds the plain (unencrypted) pack. Call <see cref="Encrypt"/> on the result.</summary>
        public static byte[] EncodePlain(IList<Level> levels) => EncodePlain(levels, "unnamed");

        public static byte[] EncodePlain(IList<Level> levels, string levelSetId)
        {
            if (string.IsNullOrWhiteSpace(levelSetId)) levelSetId = "unnamed";

            using (var payload = new MemoryStream())
            using (var pw = new BinaryWriter(payload))
            {
                var offsets = new int[levels.Count];
                for (int i = 0; i < levels.Count; i++)
                {
                    offsets[i] = (int)payload.Length;
                    var l = levels[i];
                    int n = l.size;
                    if (n < 1 || n > 15) throw new InvalidDataException($"level {i}: size {n} out of range");
                    if (l.regions == null || l.regions.Length != n * n) throw new InvalidDataException($"level {i}: bad regions");
                    if (l.solutionColumns == null || l.solutionColumns.Length != n) throw new InvalidDataException($"level {i}: bad solution");

                    pw.Write((byte)n);
                    pw.Write((ushort)(l.weight < 0 ? 0 : l.weight > ushort.MaxValue ? ushort.MaxValue : l.weight));

                    ushort revealed = 0;
                    if (l.revealedRows != null)
                        foreach (int r in l.revealedRows)
                            if (r >= 0 && r < 16) revealed |= (ushort)(1 << r);
                    pw.Write(revealed);

                    foreach (int g in l.regions) pw.Write((byte)g);
                    foreach (int c in l.solutionColumns) pw.Write((byte)c);
                    for (int k = 0; k < n; k++)
                        pw.Write((byte)(l.regionColors != null && k < l.regionColors.Length ? l.regionColors[k] : k));
                }
                pw.Flush();

                using (var file = new MemoryStream())
                using (var w = new BinaryWriter(file))
                {
                    w.Write(MagicBytes);
                    w.Write(Version);
                    w.Write(levelSetId);
                    w.Write(levels.Count);
                    foreach (int off in offsets) w.Write(off);
                    w.Write(payload.GetBuffer(), 0, (int)payload.Length);
                    w.Flush();
                    return file.ToArray();
                }
            }
        }

        // ---- decode (runtime) ----------------------------------------------------------

        /// <summary>Number of levels in a plain pack (validates magic + version).</summary>
        public static int Count(byte[] plain)
        {
            using (var h = OpenHeader(plain))
                return h.count;
        }

        /// <summary>Stable id of the exported level set stored in the pack header.</summary>
        public static string LevelSetId(byte[] plain)
        {
            using (var h = OpenHeader(plain))
                return h.levelSetId;
        }

        /// <summary>Decodes one level from a plain pack via the lookup table. O(1).</summary>
        public static Level Decode(byte[] plain, int index)
        {
            using (var h = OpenHeader(plain))
            {
                var r = h.reader;
                int count = h.count;
                if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));

                r.BaseStream.Position = h.lookupStart + index * 4L;
                int offset = r.ReadInt32();
                r.BaseStream.Position = h.payloadStart + offset;

                var l = new Level();
                int n = r.ReadByte();
                l.size = n;
                l.weight = r.ReadUInt16();

                ushort revealed = r.ReadUInt16();
                if (revealed != 0)
                {
                    var rows = new List<int>(4);
                    for (int b = 0; b < 16; b++) if ((revealed & (1 << b)) != 0) rows.Add(b);
                    l.revealedRows = rows.ToArray();
                }

                l.regions = new int[n * n];
                for (int i = 0; i < l.regions.Length; i++) l.regions[i] = r.ReadByte();
                l.solutionColumns = new int[n];
                for (int i = 0; i < n; i++) l.solutionColumns[i] = r.ReadByte();
                l.regionColors = new int[n];
                for (int i = 0; i < n; i++) l.regionColors[i] = r.ReadByte();
                return l;
            }
        }

        sealed class Header : IDisposable
        {
            public BinaryReader reader;
            public string levelSetId;
            public int count;
            public long lookupStart;
            public long payloadStart;
            public void Dispose() => reader?.Dispose();
        }

        static Header OpenHeader(byte[] plain)
        {
            var r = new BinaryReader(new MemoryStream(plain, false));
            var magic = r.ReadBytes(4);
            for (int i = 0; i < 4; i++)
                if (magic.Length < 4 || magic[i] != MagicBytes[i])
                    throw new InvalidDataException("not a level pack");
            byte version = r.ReadByte();
            if (version != Version) throw new InvalidDataException($"level pack version {version}, expected {Version}");

            string levelSetId = r.ReadString();
            int count = r.ReadInt32();
            long lookupStart = r.BaseStream.Position;
            return new Header
            {
                reader = r,
                levelSetId = levelSetId,
                count = count,
                lookupStart = lookupStart,
                payloadStart = lookupStart + count * 4L
            };
        }

        // ---- encryption ----------------------------------------------------------------

        // assembled at runtime so the key isn't one obvious constant sitting in the binary
        static byte[] Key()
        {
            var k = new byte[16];
            for (int i = 0; i < 16; i++) k[i] = (byte)((i * 47 + 113) ^ (i << 3) ^ 0x5A);
            return k;
        }

        // 16-byte IV derived from the content, so identical input encrypts to identical output.
        static byte[] DeterministicIV(byte[] content)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(content);
                var iv = new byte[16];
                Array.Copy(hash, iv, 16);
                return iv;
            }
        }

        public static byte[] Encrypt(byte[] plain)
        {
            byte[] packed = Gzip(plain);
            using (var aes = Aes.Create())
            {
                aes.Key = Key();
                // Deterministic IV derived from the content (not GenerateIV's randomness) so an
                // unchanged pool re-exports to byte-identical output - no git churn. Different
                // content still gets a different IV. The IV was never the secret (see Key()), so
                // this costs no meaningful security; Decrypt reads it back from the first 16 bytes.
                aes.IV = DeterministicIV(packed);
                using (var enc = aes.CreateEncryptor())
                {
                    byte[] cipher = enc.TransformFinalBlock(packed, 0, packed.Length);
                    var outBytes = new byte[16 + cipher.Length];
                    Array.Copy(aes.IV, outBytes, 16);
                    Array.Copy(cipher, 0, outBytes, 16, cipher.Length);
                    return outBytes;
                }
            }
        }

        public static byte[] Decrypt(byte[] file)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = Key();
                var iv = new byte[16];
                Array.Copy(file, iv, 16);
                aes.IV = iv;
                using (var dec = aes.CreateDecryptor())
                {
                    byte[] plain = dec.TransformFinalBlock(file, 16, file.Length - 16);
                    // gzip magic -> inflate; a bare QPLV pack (pre-compression export) passes through
                    return plain.Length > 2 && plain[0] == 0x1F && plain[1] == 0x8B ? Gunzip(plain) : plain;
                }
            }
        }

        static byte[] Gzip(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true))
                    gz.Write(data, 0, data.Length);
                return ms.ToArray();
            }
        }

        static byte[] Gunzip(byte[] data)
        {
            using (var src = new MemoryStream(data, false))
            using (var gz = new GZipStream(src, CompressionMode.Decompress))
            using (var dst = new MemoryStream())
            {
                gz.CopyTo(dst);
                return dst.ToArray();
            }
        }
    }
}