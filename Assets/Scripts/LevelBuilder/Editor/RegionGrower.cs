using System;
using System.Collections.Generic;

namespace QueensPuzzle
{
    /// <summary>
    /// Partitions the N x N board into N contiguous colored regions, one seeded at each
    /// queen. Uses randomized multi-source flood growth: every step a random region that can
    /// still expand claims a random unassigned 4-neighbor. The result is a set of organic,
    /// connected regions that together cover the whole board, each containing exactly one queen.
    /// </summary>
    public static class RegionGrower
    {
        /// <summary>
        /// Returns region[index] = region id for each cell, row-major (index = row * n + col).
        /// Region k is the one containing the queen in row k.
        /// </summary>
        public static int[] Grow(int n, int[] queenColumns, Random rng)
        {
            int total = n * n;
            int[] region = new int[total];
            for (int i = 0; i < total; i++) region[i] = -1;

            var frontier = new List<int>[n];
            for (int k = 0; k < n; k++)
            {
                frontier[k] = new List<int>();
                int seed = k * n + queenColumns[k];
                region[k * n + queenColumns[k]] = k;
                AddNeighbors(seed, n, region, frontier[k]);
            }

            int assigned = n;
            var active = new List<int>(n);
            while (assigned < total)
            {
                active.Clear();
                for (int k = 0; k < n; k++)
                    if (HasUnassigned(frontier[k], region)) active.Add(k);

                if (active.Count == 0) break; // unreachable on a connected board

                int k2 = active[rng.Next(active.Count)];
                int cell = PopRandomUnassigned(frontier[k2], region, rng);
                if (cell < 0) continue;

                region[cell] = k2;
                assigned++;
                AddNeighbors(cell, n, region, frontier[k2]);
            }
            return region;
        }

        /// <summary>
        /// Tries to edit the region map so the alternative solution <paramref name="alt"/> is no
        /// longer valid, while keeping the target <paramref name="solutionColumns"/> valid and all
        /// regions contiguous. Works by taking a cell where <paramref name="alt"/> places a queen but
        /// the target does not, and moving it into a neighboring region: that cell can never be a
        /// target queen (the target has its only queen in that row elsewhere), so the target is
        /// unaffected, but <paramref name="alt"/> now double-books one region and is broken.
        /// Returns false if no safe move exists (caller should re-grow from scratch).
        /// </summary>
        public static bool TryBreakAlternative(int n, int[] region, int[] solutionColumns, int[] alt, Random rng)
        {
            var wrongRows = new List<int>();
            for (int r = 0; r < n; r++)
                if (alt[r] != solutionColumns[r]) wrongRows.Add(r);
            Shuffle(wrongRows, rng);

            foreach (int r in wrongRows)
            {
                int idx = r * n + alt[r];
                int from = region[idx];

                var targets = NeighborRegions(idx, n, region, from);
                Shuffle(targets, rng);

                foreach (int to in targets)
                {
                    if (!RegionStaysConnectedWithout(n, region, from, idx)) continue;
                    region[idx] = to;
                    return true;
                }
            }
            return false;
        }

        static List<int> NeighborRegions(int index, int n, int[] region, int exclude)
        {
            var result = new List<int>(4);
            int r = index / n, c = index % n;
            AddRegion(r - 1, c, n, region, exclude, result);
            AddRegion(r + 1, c, n, region, exclude, result);
            AddRegion(r, c - 1, n, region, exclude, result);
            AddRegion(r, c + 1, n, region, exclude, result);
            return result;
        }

        static void AddRegion(int r, int c, int n, int[] region, int exclude, List<int> result)
        {
            if (r < 0 || r >= n || c < 0 || c >= n) return;
            int reg = region[r * n + c];
            if (reg != exclude && !result.Contains(reg)) result.Add(reg);
        }

        /// <summary>True if region <paramref name="g"/> would remain a single connected, non-empty
        /// component after removing the cell at <paramref name="excluded"/>.</summary>
        static bool RegionStaysConnectedWithout(int n, int[] region, int g, int excluded)
        {
            int total = 0, start = -1;
            for (int i = 0; i < region.Length; i++)
            {
                if (region[i] != g || i == excluded) continue;
                total++;
                if (start < 0) start = i;
            }
            if (total == 0) return false; // never strip a region down to nothing

            var seen = new bool[n * n];
            var stack = new Stack<int>();
            stack.Push(start);
            seen[start] = true;
            int reached = 0;
            while (stack.Count > 0)
            {
                int idx = stack.Pop();
                reached++;
                int r = idx / n, c = idx % n;
                PushIf(r - 1, c); PushIf(r + 1, c); PushIf(r, c - 1); PushIf(r, c + 1);

                void PushIf(int rr, int cc)
                {
                    if (rr < 0 || rr >= n || cc < 0 || cc >= n) return;
                    int j = rr * n + cc;
                    if (!seen[j] && j != excluded && region[j] == g) { seen[j] = true; stack.Push(j); }
                }
            }
            return reached == total;
        }

        static void AddNeighbors(int index, int n, int[] region, List<int> frontier)
        {
            int r = index / n, c = index % n;
            TryAdd(r - 1, c, n, region, frontier);
            TryAdd(r + 1, c, n, region, frontier);
            TryAdd(r, c - 1, n, region, frontier);
            TryAdd(r, c + 1, n, region, frontier);
        }

        static void TryAdd(int r, int c, int n, int[] region, List<int> frontier)
        {
            if (r < 0 || r >= n || c < 0 || c >= n) return;
            int idx = r * n + c;
            if (region[idx] == -1) frontier.Add(idx);
        }

        static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        static bool HasUnassigned(List<int> frontier, int[] region)
        {
            for (int i = 0; i < frontier.Count; i++)
                if (region[frontier[i]] == -1) return true;
            return false;
        }

        static int PopRandomUnassigned(List<int> frontier, int[] region, Random rng)
        {
            // Compact out already-claimed cells, then pick a random remaining one.
            int write = 0;
            for (int i = 0; i < frontier.Count; i++)
                if (region[frontier[i]] == -1) frontier[write++] = frontier[i];
            if (write == 0) { frontier.Clear(); return -1; }
            frontier.RemoveRange(write, frontier.Count - write);

            int pick = rng.Next(frontier.Count);
            int cell = frontier[pick];
            frontier[pick] = frontier[frontier.Count - 1];
            frontier.RemoveAt(frontier.Count - 1);
            return cell;
        }
    }
}
