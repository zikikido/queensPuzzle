using System.Collections.Generic;
using UnityEngine;

namespace QueensPuzzle.EditorTools
{
    /// <summary>
    /// Builds a <see cref="LevelData"/> from a pasted region grid — the colours of a board,
    /// without the queens. The grid is one row per line; each cell is either a whitespace-
    /// separated token ("A A B") or a single character ("AAB"). Labels are arbitrary: the
    /// order in which they first appear assigns region ids 0..N-1.
    ///
    /// We have only the colours, so the board is solved to recover the queens; the import is
    /// rejected unless that solution is unique. On success the level is rated and traced just
    /// like a generated one.
    /// </summary>
    public static class LevelImporter
    {
        /// <summary>Parses, solves and rates the grid. Returns null (with a reason in
        /// <paramref name="error"/>) if it isn't a valid, unique puzzle.</summary>
        public static LevelData Import(string text, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(text)) { error = "Paste a region grid first."; return null; }

            var rows = new List<string>();
            foreach (var raw in text.Replace("\r", "").Split('\n'))
                if (raw.Trim().Length > 0) rows.Add(raw.Trim());

            int n = rows.Count;
            if (n < LevelGenerator.MinSize || n > LevelGenerator.MaxSize)
            {
                error = $"Got {n} rows — board size must be {LevelGenerator.MinSize}..{LevelGenerator.MaxSize}.";
                return null;
            }

            var ids = new Dictionary<string, int>();
            int[] region = new int[n * n];

            for (int r = 0; r < n; r++)
            {
                string[] cells = Tokenize(rows[r]);
                if (cells.Length != n)
                {
                    error = $"Row {r + 1} has {cells.Length} cells, expected {n}.";
                    return null;
                }

                for (int c = 0; c < n; c++)
                {
                    string label = cells[c];
                    if (!ids.TryGetValue(label, out int id))
                    {
                        id = ids.Count;
                        if (id >= n)
                        {
                            error = $"More than {n} distinct colours — an {n}x{n} board needs exactly {n}.";
                            return null;
                        }
                        ids[label] = id;
                    }
                    region[r * n + c] = id;
                }
            }

            if (ids.Count != n)
            {
                error = $"Found {ids.Count} distinct colours, but an {n}x{n} board needs exactly {n}.";
                return null;
            }

            if (!QueensSolver.TrySolve(n, region, out int[] cols, out bool unique))
            {
                error = "No valid solution — this colour layout can't be solved.";
                return null;
            }
            if (!unique)
            {
                error = "Not a unique puzzle — this layout has more than one solution.";
                return null;
            }

            var data = ScriptableObject.CreateInstance<LevelData>();
            data.size = n;
            data.regions = region;
            data.solutionColumns = cols;
            data.seed = 0; // imported, not generated from a seed

            var rating = DifficultyRater.Rate(n, region, cols);
            data.difficulty = rating.difficulty;
            data.estimatedSolveSeconds = rating.estimatedSeconds;
            return data;
        }

        /// <summary>Splits a row into cells: whitespace-separated when there are spaces/tabs,
        /// otherwise one character per cell.</summary>
        static string[] Tokenize(string line)
        {
            if (line.IndexOf(' ') >= 0 || line.IndexOf('\t') >= 0)
                return line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);

            var cells = new string[line.Length];
            for (int i = 0; i < line.Length; i++) cells[i] = line[i].ToString();
            return cells;
        }
    }
}
