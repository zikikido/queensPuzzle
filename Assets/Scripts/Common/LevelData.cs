using UnityEngine;

namespace QueensPuzzle
{
    /// <summary>
    /// A single puzzle as an EDITOR authoring asset (.asset). The runtime never touches this
    /// type — shipped levels are packed into levels.bytes and played as <see cref="LevelPack.Level"/>;
    /// <see cref="ToPacked"/> is the bridge (export, editor playtests, dedup hashing).
    /// The board is always square: <see cref="size"/> = number of regions = number of queens.
    /// Arrays are stored flat because Unity cannot serialize multi-dimensional arrays.
    /// </summary>
    [CreateAssetMenu(fileName = "Level", menuName = "QueensPuzzle/Level", order = 0)]
    public class LevelData : ScriptableObject
    {
        [Tooltip("N — the board is N x N, with N regions and N queens.")]
        public int size;

        [Tooltip("Region id [0..N-1] for each cell, row-major: index = row * size + col.")]
        public int[] regions;

        [Tooltip("Solution: solutionColumns[row] = column of the queen in that row.")]
        public int[] solutionColumns;

        [Tooltip("Measured solve weight — total find+think effort; higher = harder. 0 until rated.")]
        public int weight;

        [Tooltip("RNG seed that produced this level — lets us reproduce it exactly.")]
        public int seed;

        [Tooltip("Rows whose solution queen starts revealed on the board (early-level help). Empty = none. The column derives from solutionColumns, so a revealed queen can never contradict the solution.")]
        public int[] revealedRows;

        [Tooltip("Optional palette (SORegionsColors) index per region id — painted levels keep their authored colours. Null/empty = region k uses colour k.")]
        public int[] regionColors;

        /// <summary>Region id at the given cell.</summary>
        public int RegionAt(int row, int col) => regions[row * size + col];

        /// <summary>Palette (SORegionsColors) index a region is shown with — authored levels can
        /// pick their colours; everything else falls back to "region k = colour k".</summary>
        public int ColorOf(int region) =>
            regionColors != null && region >= 0 && region < regionColors.Length ? regionColors[region] : region;

        /// <summary>True if the queen in the solution sits at this cell.</summary>
        public bool IsSolutionQueen(int row, int col) => solutionColumns[row] == col;

        /// <summary>True if this row's solution queen starts revealed on the board.</summary>
        public bool IsRevealedRow(int row) => revealedRows != null && System.Array.IndexOf(revealedRows, row) >= 0;

        /// <summary>The runtime form of this level — what the exporter packs and the game plays.</summary>
        public LevelPack.Level ToPacked() => new LevelPack.Level
        {
            size = size,
            weight = weight,
            regions = regions,
            solutionColumns = solutionColumns,
            revealedRows = revealedRows,
            regionColors = regionColors,
        };

        /// <summary>Stable hash of the playable content — the single implementation lives on
        /// <see cref="LevelPack.Level.ContentHash"/>; this is the editor-side convenience.</summary>
        public int ContentHash() => ToPacked().ContentHash();
    }
}
