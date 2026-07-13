using System;
using System.Collections.Generic;
using QueensPuzzle.EditorTools;

namespace QueensPuzzle
{
    /// <summary>
    /// Completes a partly painted board into a valid unique level. Painted cells are LOCKED to
    /// their colour — the drawing survives pixel-exact. Empty cells (-1) are covered by the
    /// colours the drawing didn't use, each seeded at a freshly searched queen; when the drawing
    /// already uses all N colours, the painted regions grow into the gaps instead.
    ///
    /// Per attempt: search a solution whose queens give every painted colour exactly one queen
    /// (the leftover queens land on empty cells and seed the new regions) → flood the empty
    /// cells → repair to uniqueness moving only originally-empty cells. Any stage failing
    /// re-rolls, up to the caller's attempt budget.
    /// </summary>
    public static class StencilFiller
    {
        public static LevelData Fill(int n, int[] paint, int attempts, out string error,
            Action<float> onProgress = null)
        {
            error = null;
            if (n < LevelGenerator.MinSize || n > LevelGenerator.MaxSize)
            { error = $"Board size must be {LevelGenerator.MinSize}..{LevelGenerator.MaxSize}."; return null; }

            // fully painted → the plain Build path (solve + uniqueness check as-is)
            bool anyFree = false, anyPainted = false;
            foreach (int id in paint)
            {
                if (id >= n) { error = "painted colour out of range."; return null; }
                if (id >= 0) anyPainted = true; else anyFree = true;
            }
            if (!anyPainted) { error = "nothing painted — draw something first."; return null; }
            if (!anyFree) return LevelImporter.BuildFromRegions(n, (int[])paint.Clone(), out error);

            error = ValidateStencil(n, paint);
            if (error != null) return null;

            if (!TryFillRaw(n, paint, attempts, Environment.TickCount, out int[] region, onProgress))
            {
                error = $"no unique completion in {attempts} attempts — loosen the drawing (more empty "
                      + "cells, no sealed-off pockets) or raise the attempts.";
                return null;
            }
            return LevelImporter.BuildFromRegions(n, region, out error);   // final verify + rate
        }

        /// <summary>
        /// Drawings that can never fill, caught before burning attempts: a colour owning two or
        /// more FULL rows (or columns) — every full row forces that row's queen into the colour,
        /// and a colour gets exactly one queen. Returns the reason, or null when the stencil is fine.
        /// </summary>
        public static string ValidateStencil(int n, int[] paint)
        {
            var rowsOf = new Dictionary<int, List<int>>();
            var colsOf = new Dictionary<int, List<int>>();

            for (int r = 0; r < n; r++)
            {
                int id = paint[r * n];
                if (id < 0) continue;
                bool full = true;
                for (int c = 1; c < n; c++) if (paint[r * n + c] != id) { full = false; break; }
                if (!full) continue;
                if (!rowsOf.TryGetValue(id, out var list)) rowsOf[id] = list = new List<int>();
                list.Add(r);
            }
            for (int c = 0; c < n; c++)
            {
                int id = paint[c];
                if (id < 0) continue;
                bool full = true;
                for (int r = 1; r < n; r++) if (paint[r * n + c] != id) { full = false; break; }
                if (!full) continue;
                if (!colsOf.TryGetValue(id, out var list)) colsOf[id] = list = new List<int>();
                list.Add(c);
            }

            foreach (var kv in rowsOf)
                if (kv.Value.Count >= 2)
                    return $"colour {kv.Key + 1} completely fills rows {string.Join(", ", kv.Value)} — "
                         + "each full row forces its queen into that colour, and a colour holds only one queen.";
            foreach (var kv in colsOf)
                if (kv.Value.Count >= 2)
                    return $"colour {kv.Key + 1} completely fills columns {string.Join(", ", kv.Value)} — "
                         + "each full column forces its queen into that colour, and a colour holds only one queen.";

            // ---- pockets: each sealed-off empty area needs its own new region, seeded by a queen inside it

            var usedIds = new HashSet<int>();
            foreach (int id in paint) if (id >= 0) usedIds.Add(id);
            int unusedColours = n - usedIds.Count;

            var pocketOf = new int[n * n];   // -1 = painted, else pocket index
            int pockets = 0;
            for (int i = 0; i < paint.Length; i++) pocketOf[i] = -1;
            var stack = new Stack<int>();
            for (int i = 0; i < paint.Length; i++)
            {
                if (paint[i] >= 0 || pocketOf[i] >= 0) continue;
                stack.Push(i);
                pocketOf[i] = pockets;
                while (stack.Count > 0)
                {
                    int idx = stack.Pop();
                    int r = idx / n, c = idx % n;
                    Visit(r - 1, c); Visit(r + 1, c); Visit(r, c - 1); Visit(r, c + 1);

                    void Visit(int rr, int cc)
                    {
                        if (rr < 0 || rr >= n || cc < 0 || cc >= n) return;
                        int j = rr * n + cc;
                        if (paint[j] < 0 && pocketOf[j] < 0) { pocketOf[j] = pockets; stack.Push(j); }
                    }
                }
                pockets++;
            }

            if (pockets > unusedColours)
                return $"the drawing seals the empty space into {pockets} separate areas, but only "
                     + $"{unusedColours} unused colour(s) remain to fill them — open a passage or use fewer colours.";

            // a pocket confined to one row can only be seeded by that row's queen — two in a row is impossible
            var pocketRows = new int[pockets]; var pocketCols = new int[pockets];
            for (int p = 0; p < pockets; p++) { pocketRows[p] = -2; pocketCols[p] = -2; }   // -2 = unseen, -1 = mixed
            for (int i = 0; i < paint.Length; i++)
            {
                int p = pocketOf[i];
                if (p < 0) continue;
                int r = i / n, c = i % n;
                pocketRows[p] = pocketRows[p] == -2 || pocketRows[p] == r ? r : -1;
                pocketCols[p] = pocketCols[p] == -2 || pocketCols[p] == c ? c : -1;
            }
            var rowPockets = new Dictionary<int, int>();
            var colPockets = new Dictionary<int, int>();
            for (int p = 0; p < pockets; p++)
            {
                if (pocketRows[p] >= 0)
                {
                    rowPockets.TryGetValue(pocketRows[p], out int k);
                    if (k == 1) return $"row {pocketRows[p]} has two separate sealed empty areas — each needs "
                                     + "that row's queen, and a row has only one. Open a passage between them.";
                    rowPockets[pocketRows[p]] = 1;
                }
                if (pocketCols[p] >= 0)
                {
                    colPockets.TryGetValue(pocketCols[p], out int k);
                    if (k == 1) return $"column {pocketCols[p]} has two separate sealed empty areas — each needs "
                                     + "that column's queen, and a column has only one. Open a passage between them.";
                    colPockets[pocketCols[p]] = 1;
                }
            }
            return null;
        }

        /// <summary>Pure core (no Unity types): completes the stencil into a unique region map.</summary>
        public static bool TryFillRaw(int n, int[] paint, int attempts, int seed, out int[] region,
            Action<float> onProgress = null)
        {
            var usedIds = new HashSet<int>();
            foreach (int id in paint) if (id >= 0) usedIds.Add(id);

            var newIds = new List<int>();           // colours the drawing didn't use — they fill the space
            for (int k = 0; k < n; k++) if (!usedIds.Contains(k)) newIds.Add(k);

            var heroIds = new List<int>(usedIds);   // grow-mode fallback when all N colours are painted

            var rng = new Random(seed);
            for (int a = 0; a < attempts; a++)
            {
                onProgress?.Invoke(a / (float)attempts);
                if (TryOnce(n, paint, newIds, heroIds, rng, out region)) return true;
            }
            region = null;
            return false;
        }

        static bool TryOnce(int n, int[] paint, List<int> newIds, List<int> heroIds, Random rng, out int[] region)
        {
            region = null;
            int[] cols = FindSolution(n, paint, newIds.Count, rng);
            if (cols == null) return false;

            var r = (int[])paint.Clone();
            List<int> growIds;
            if (newIds.Count > 0)
            {
                // seed each unused colour at one of the free-cell queens (row order is as good as any)
                growIds = newIds;
                int next = 0;
                for (int row = 0; row < n; row++)
                {
                    int idx = row * n + cols[row];
                    if (paint[idx] < 0) r[idx] = newIds[next++];
                }
            }
            else
                growIds = heroIds;   // all N colours painted — the drawing itself absorbs the gaps

            if (!RegionGrower.GrowPartial(n, r, growIds, rng)) return false;   // unreachable pocket

            // repair to uniqueness, never touching a painted cell
            var locked = new bool[n * n];
            for (int i = 0; i < locked.Length; i++) locked[i] = paint[i] >= 0;
            int budget = n * n * 4;
            for (int i = 0; i < budget; i++)
            {
                if (!QueensSolver.TryFindAlternative(n, r, cols, out int[] alt)) { region = r; return true; }
                if (!RegionGrower.TryBreakAlternative(n, r, cols, alt, rng, locked)) return false;
            }
            return false;
        }

        /// <summary>
        /// Backtracking queen search honouring the stencil: valid Queens placement (one per
        /// row/column, no touching) where every painted colour gets exactly ONE queen, and at most
        /// <paramref name="maxFreeQueens"/> queens land on empty cells (those seed the new regions —
        /// with each painted colour at exactly one, the free count comes out exact by itself).
        /// </summary>
        static int[] FindSolution(int n, int[] paint, int maxFreeQueens, Random rng)
        {
            // last row each painted colour appears in — for the "must place now" pruning
            var lastRow = new Dictionary<int, int>();
            for (int i = 0; i < paint.Length; i++)
                if (paint[i] >= 0) lastRow[paint[i]] = i / n;

            var cols = new int[n];
            var usedCol = new bool[n];
            var heroCount = new Dictionary<int, int>();
            foreach (int id in lastRow.Keys) heroCount[id] = 0;
            int freeUsed = 0;

            bool Place(int row)
            {
                if (row == n)
                {
                    foreach (var kv in heroCount) if (kv.Value != 1) return false;
                    return true;
                }

                // colours whose final chance is this row and still have no queen
                int mustId = int.MinValue;
                foreach (var kv in lastRow)
                    if (kv.Value == row && heroCount[kv.Key] == 0)
                    {
                        if (mustId != int.MinValue) return false;   // two colours need this row — dead end
                        mustId = kv.Key;
                    }

                var candidates = new List<int>(n);
                for (int c = 0; c < n; c++)
                {
                    if (usedCol[c]) continue;
                    if (row > 0 && Math.Abs(c - cols[row - 1]) < 2) continue;

                    int id = paint[row * n + c];
                    if (mustId != int.MinValue && id != mustId) continue;
                    if (id >= 0 && heroCount[id] >= 1) continue;    // colour already has its queen
                    if (id < 0 && freeUsed >= maxFreeQueens) continue;
                    candidates.Add(c);
                }
                Shuffle(candidates, rng);

                foreach (int c in candidates)
                {
                    int id = paint[row * n + c];
                    cols[row] = c;
                    usedCol[c] = true;
                    if (id >= 0) heroCount[id]++; else freeUsed++;

                    if (Place(row + 1)) return true;

                    usedCol[c] = false;
                    if (id >= 0) heroCount[id]--; else freeUsed--;
                }
                return false;
            }

            return Place(0) ? cols : null;
        }

        static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
