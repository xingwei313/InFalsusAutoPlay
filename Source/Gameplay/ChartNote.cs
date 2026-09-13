namespace InFalsusAutoPlay
{
    /// <summary>One note, reduced to what the mod needs.</summary>
    internal readonly struct ChartNote
    {
        internal readonly int StartMs;      // absolute chart time of the judgement moment
        internal readonly int EndMs;        // == StartMs for a tap
        internal readonly int LaneFirst;
        internal readonly int LaneLast;
        internal readonly int Type;         // _HA
        internal readonly int Side;         // _CA
        internal readonly float StartX;     // centre of the bar at progress 0
        internal readonly float EndX;       // centre of the bar at progress 1
        internal readonly float StartWidth; // full width at progress 0
        internal readonly float EndWidth;   // full width at progress 1
        internal readonly int Flags;        // _FA; bits 0x08/0x10 pick the left edge easing, 0x40/0x80 the right
        internal readonly long NoteId;      // key into the judgement-group dictionary

        internal ChartNote(int startMs, int endMs, int laneFirst, int laneLast, int type, int side,
                           float startX, float endX, float startWidth, float endWidth, int flags,
                           long noteId)
        {
            StartMs = startMs;
            EndMs = endMs;
            LaneFirst = laneFirst;
            LaneLast = laneLast;
            Type = type;
            Side = side;
            StartX = startX;
            EndX = endX;
            StartWidth = startWidth;
            EndWidth = endWidth;
            Flags = flags;
            NoteId = noteId;
        }

        internal bool IsSky => Side == Offsets.Plane.Sky;

        /// <summary>
        /// A note the game holds open. The threshold only has to separate a tap from a note with a
        /// real duration; taps have EndMs == StartMs exactly.
        /// </summary>
        internal bool IsHold => EndMs - StartMs > 40;
    }
}
