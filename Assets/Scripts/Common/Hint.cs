namespace QueensPuzzle
{
    public enum HintKind
    {
        WrongQueen,   // a placed queen that isn't a real solution cell — remove it
        WrongX,       // an X on a cell that must hold a queen — clear it
        PlaceQueen,   // a cell that is forced to be a queen
        Eliminate,    // cells that can be X'd by the next deduction
        Guess         // no forced move — the best place to guess (fewest options), cells = those options
    }

    /// <summary>One suggested next action for the player. <see cref="cells"/> are board indices (row*n + col).</summary>
    public struct Hint
    {
        public HintKind kind;
        public int[] cells;       // the cells to mark/unmark — the step's targets
        public int[] causeCells;  // the cells the reasoning is ABOUT (shown tinted, never editable); may be null
        public string note;
    }
}
