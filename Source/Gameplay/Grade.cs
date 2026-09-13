namespace InFalsusAutoPlay
{
    /// <summary>
    /// _FH — what the game decided for a group. Read out of `_cH` (+0x20) and tallied by value;
    /// grades only run 0..6, so a byte array indexed by grade is enough and 255 can stand for
    /// "could not be read".
    ///
    /// The names of these, for the log lines, are in Source\Probe: nothing that ships reads them.
    /// </summary>
    internal static class Grade
    {
        internal const byte None = 0;
        internal const byte Miss = 1;
        internal const byte FarEarly = 2;
        internal const byte FarLate = 3;
        internal const byte NearEarly = 4;
        internal const byte NearLate = 5;
        internal const byte Perfect = 6;

        /// <summary>
        /// Stands in for a grade when the note's judgement group could not be found. Grades only
        /// run 0..6, so this cannot collide with a real one.
        /// </summary>
        internal const byte Unreadable = 255;
    }
}
