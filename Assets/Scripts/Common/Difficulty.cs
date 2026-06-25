namespace QueensPuzzle
{
    /// <summary>
    /// Logical difficulty of a level, measured by the hardest deduction technique
    /// the solver is forced to use. <see cref="Unrated"/> means the rating step has
    /// not run yet (e.g. the generator produced a valid puzzle but difficulty is TBD).
    /// </summary>
    public enum Difficulty
    {
        Unrated = 0,
        Kitten = 1,
        Easy = 2,
        Medium = 3,
        Hard = 4,
        Expert = 5,
    }
}
