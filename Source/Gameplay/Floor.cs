using System;
using System.Collections.Generic;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// The floor lanes: at the start of every gameplay tick, make the game's input state be whatever
    /// a perfect player's would be at that instant.
    ///
    /// Nothing here judges, scores or spawns anything. `_VD._Oz` does all of that from the input
    /// state, so supplying the input makes the game produce a real perfect run — grades, combo,
    /// shield, hit effects and the end-of-song settlement all come out of the game's own code and
    /// cannot disagree with each other.
    ///
    /// The press written is byte-for-byte the one the game's own key handler produces
    /// (`_VD._Pz` at 0x1805F5CE0):
    ///
    ///     mov byte ptr [lane*24 + 0x20], 1     ; $jAA — the press edge _pz keys on
    ///     mov byte ptr [lane*24 + 0x21], 1     ; $JAA — held
    ///
    /// `_VD._pz` grades a note Perfect when its time is within 25 ms and the edge is set:
    ///
    ///     if ( fabs(deltaMs) &lt;= 25.0 ) grade = 6;
    ///     if ( pressEdge || grade == 1 ) { group.judged = 1; group.grade = grade; }
    ///
    /// so pressing inside that window is the whole trick. Writing only `$JAA` — the held flag, with
    /// no edge — leaves pressEdge false and the game judges nothing at all.
    ///
    /// The sky plane is not handled here. Its notes are not played by pressing lanes and its grading
    /// does not read the input array at all; see <see cref="Sky"/> and <see cref="SkyBar"/>.
    /// </summary>
    internal sealed unsafe class Floor
    {
        /// <summary>
        /// Do not fake a press for a note already this far in the past. Past 100 ms the game has
        /// graded the note Miss on its own, so a press there cannot help.
        /// </summary>
        private const double CatchUpMs = 120.0;

        /// <summary>Chart time at which a lane must be released; <see cref="NotHeld"/> when nothing is held.</summary>
        private const double NotHeld = -1.0;

        private readonly Chart _chart;

        /// <summary>Which lanes autoplay is holding, and until when. The only record of lane ownership.</summary>
        private readonly double[] _releaseAtMs = new double[Offsets.Plane.LaneCount];

        /// <summary>
        /// Set by <see cref="ClearLanes"/>, taken by the next <see cref="Release"/>.
        ///
        /// Dropping the schedule is not enough on its own: forgetting stops the held flag being
        /// written, but nothing then clears a flag already written, and `$JAA` left set is what
        /// `_VD._Uz` reads to decide a judged note is still being held.
        /// </summary>
        private bool _clearAll;

        /// <summary>Every note already pressed, by id — a chart re-read can insert notes anywhere.</summary>
        private readonly HashSet<long> _pressed = new HashSet<long>();

        private int _next;
        private int _revision = -1;

        // Read by the status line only.
        internal long Ticks;
        internal long Presses;
        internal long Late;
        internal int Cursor => _next;

        internal Floor(Chart chart)
        {
            _chart = chart;
            ClearLanes();
        }

        /// <summary>
        /// Per-song. The release schedule has just been dropped, and it is the only thing that ever
        /// clears a held lane, so the next release pass is told to let go of every lane — including
        /// ones whose note never finished.
        /// </summary>
        internal void ClearLanes()
        {
            for (int i = 0; i < _releaseAtMs.Length; i++) _releaseAtMs[i] = NotHeld;
            _clearAll = true;
        }

        /// <summary>Called from the `_Oz` detour, before the original body runs.</summary>
        internal void Tick(IntPtr lanes, double tickSeconds, double nowMs)
        {
            // With the floor lanes switched off the input array is left completely alone — including
            // the per-tick edge clear below, which would otherwise eat a real player's key presses.
            if (Config.Floors)
            {
                Ticks++;

                if (_chart.Revision != _revision) Resync(nowMs);

                Release(lanes, tickSeconds, nowMs);

                if (_chart.Loaded) Press(lanes, tickSeconds, nowMs);
            }
        }

        // ---------------------------------------------------------------- pressing

        /// <summary>
        /// Presses every note whose time has arrived. The chart is sorted by time, so a single
        /// advancing index is enough — no per-tick scan.
        ///
        /// A note that spans several lanes is pressed on a single lane, never across its whole width.
        ///
        /// `_VD._pz` scans the note's lane range for the first lane carrying an unconsumed press
        /// edge, and consumes that one lane only:
        ///
        ///     for (lane = note.first; lane &lt;= note.last; ++lane)
        ///         if (input[lane].edge &amp;&amp; !input[lane].consumed &amp;&amp; !found)
        ///             { found = true; edgeLane = lane; }
        ///     input[edgeLane].consumed = 1;
        ///
        /// So pressing the whole width leaves every other lane with `$jAA` set and `$kAA` clear for
        /// the rest of the tick. That is not harmless: a note 100-150 ms in the future grades itself
        /// `pressEdge` — which is to say an immediate Miss — when it sees a live edge in its range,
        /// and `_pz` runs over every pending note in the same tick. Pressing a wide note across its
        /// width therefore arms a Miss on whatever else shares those lanes, which is why the extra
        /// lanes lose notes rather than help.
        ///
        /// Pressing the range's first lane alone produces the same edge on the same lane `_pz` would
        /// have chosen anyway, and leaves nothing armed. `$JAA` is written on that lane alone too,
        /// which is enough for the hold-note visuals: `_VD._Uz` ORs `isPressed` across the note's
        /// whole lane range.
        ///
        /// At most one note per lane per tick, for the same reason. `_pz` consumes the press edge for
        /// the first note it grades, so a second note in the same lane would find `$jAA` set but
        /// already spent, never be graded by the edge, and eventually be recorded as a Miss. Holding
        /// the later note over to the next tick gives it a fresh edge of its own. A human needs two
        /// separate key presses to do the same thing.
        /// </summary>
        private void Press(IntPtr lanes, double tickSeconds, double nowMs)
        {
            ChartNote[] notes = _chart.Notes;
            uint pressedThisTick = 0;

            while (_next < notes.Length && notes[_next].StartMs <= nowMs + Config.PressLeadMs)
            {
                ChartNote n = notes[_next];

                // Only the three keyboard planes are played by pressing lanes. The sky plane has
                // lane indices that do not address the six keys — its notes are graded from their
                // own geometry, not from the input array. Side 0 means "no plane" and is not
                // playable.
                if (n.Side < Offsets.Plane.Main || n.Side > Offsets.Plane.Space)
                {
                    _next++;
                    continue;
                }

                // Already pressed. Only reachable when a re-read has moved the cursor back over a
                // note, which is exactly the case this guards: a second press would leave a fresh
                // edge on a lane whose note has already consumed one.
                if (_pressed.Contains(n.NoteId))
                {
                    _next++;
                    continue;
                }

                if (nowMs - n.StartMs > CatchUpMs)
                {
                    _next++;
                    Late++;
                    if (Late <= 5)
                        Diagnostics.Warn($"note at {n.StartMs}ms was {nowMs - n.StartMs:F0}ms late; skipped");
                    continue;
                }

                int first = Math.Max(0, n.LaneFirst);
                int last = Math.Min(Offsets.Plane.LaneCount - 1, n.LaneLast);
                if (last < first) { _next++; continue; }

                uint mask = 1u << first;

                // Blocked by a note already pressed this tick: leave the cursor here and try again
                // next tick, when the edge is free.
                if ((pressedThisTick & mask) != 0) return;

                pressedThisTick |= mask;
                _next++;
                _pressed.Add(n.NoteId);

                if (!Config.DryRun)
                {
                    IntPtr e = Memory.Lane(lanes, first);
                    *(byte*)(e + Offsets.InputLane.PressEdge) = 1;
                    *(byte*)(e + Offsets.InputLane.Held) = 1;
                    *(double*)(e + Offsets.InputLane.LastInput) = tickSeconds;
                }

                _releaseAtMs[first] = Math.Max(_releaseAtMs[first],
                    n.IsHold ? n.EndMs + Config.HoldSlackMs : n.StartMs + Config.TapHoldMs);

                Presses++;
            }
        }

        // ---------------------------------------------------------------- releasing

        /// <summary>
        /// Brings each lane's state to what autoplay intends it to be, immediately before `_Oz`
        /// reads it.
        ///
        /// A lane autoplay is holding is re-asserted every tick, not just written once when the
        /// press happens. That is the point of this function. The game's key-release handler
        /// (`sub_1805F8A80`, the mirror of `_VD._Pz`) clears the lane's held flag and then calls
        /// `_Oz` in the same breath:
        ///
        ///     input[lane].held = 0;  input[lane].lastInput = t;  _VD._Oz(...)
        ///
        /// so a player tapping the very key autoplay is holding for a hold note drops the hold out
        /// from under it, and `_pz`'s hold branch — which scans the note's lanes for a held one and
        /// grades from the best of them — finds nothing held and grades from `$lAA` instead. Only
        /// re-asserting the flag after every input event can survive that.
        ///
        /// The first tick after a reset lets go of every lane, including ones whose note never
        /// finished — a rewind or a restart mid-hold otherwise leaves `$JAA` set for the rest of the
        /// song, and `$JAA` is what `_VD._Uz` reads to decide a judged note is still being held.
        /// </summary>
        private void Release(IntPtr lanes, double tickSeconds, double nowMs)
        {
            bool clearAll = _clearAll;
            _clearAll = false;

            for (int lane = 0; lane < Offsets.Plane.LaneCount; lane++)
            {
                if (clearAll || Expired(lane, nowMs)) _releaseAtMs[lane] = NotHeld;

                // The schedule above is maintained either way — it is what the dry-run trace prints —
                // but nothing reaches the lanes while dry-running.
                if (Config.DryRun) continue;

                IntPtr e = Memory.Lane(lanes, lane);

                // Cleared on every lane. The game's own per-frame clear is not enough here: a live edge
                // no note consumes makes _pz commit a Miss on the next note within 100-150 ms of it, and
                // this is the last point before _Oz reads.
                *(byte*)(e + Offsets.InputLane.PressEdge) = 0;

                if (_releaseAtMs[lane] == NotHeld)
                {
                    // Nobody's lane: strip whatever the keyboard left in it. $JAA is not merely a
                    // visual — _pz's hold branch scans the note's lanes for a held one, so a stray held
                    // lane changes a hold note's grade. $lAA is the timestamp that same scan uses for a
                    // lane that is not held, so it has to read as "never pressed" rather than as a
                    // moment ago.
                    *(byte*)(e + Offsets.InputLane.Held) = 0;
                    *(double*)(e + Offsets.InputLane.LastInput) = 0.0;
                }
                else
                {
                    // Ours, for as long as the note lasts: re-assert the hold against the key-release
                    // path, and keep the timestamp current so that if the flag is ever read as clear for
                    // a tick, the lane still grades as held-now rather than as pressed long ago.
                    *(byte*)(e + Offsets.InputLane.Held) = 1;
                    *(double*)(e + Offsets.InputLane.LastInput) = tickSeconds;
                }
            }
        }

        private bool Expired(int lane, double nowMs) =>
            _releaseAtMs[lane] != NotHeld && nowMs >= _releaseAtMs[lane];

        /// <summary>
        /// The chart was (re)published, so the arrays the cursor indexes have been replaced.
        ///
        /// The cursor is put back at the first note that is not already too late to press. It may
        /// move backwards, and that is safe only because <see cref="_pressed"/> is authoritative:
        /// walking over a note again either finds it already pressed and skips it, or finds it
        /// genuinely unpressed and presses it — which is the right answer, and is the case a
        /// forward-only cursor would strand when a re-read removes or reorders notes ahead of it.
        ///
        /// Notes before the target are at least CatchUpMs in the past and are stepped over by the
        /// same arithmetic that put the cursor there, so they are not re-counted as late.
        /// </summary>
        private void Resync(double nowMs)
        {
            _revision = _chart.Revision;

            ChartNote[] notes = _chart.Notes;

            int target = 0;
            while (target < notes.Length && notes[target].StartMs + CatchUpMs <= nowMs) target++;

            _next = target;
        }

        /// <summary>Six characters, one per lane: `X` held, `.` free. For the log's schedule trace.</summary>
        internal string ScheduleText()
        {
            var chars = new char[_releaseAtMs.Length];
            for (int i = 0; i < chars.Length; i++) chars[i] = _releaseAtMs[i] != NotHeld ? 'X' : '.';
            return new string(chars);
        }
    }
}
