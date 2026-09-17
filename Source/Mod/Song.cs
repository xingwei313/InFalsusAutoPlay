using System;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// One song's worth of state, and the order the frame runs in.
    ///
    /// Constructing one of these is what "forget the last song" means: the parts it owns all start out
    /// empty or unarmed, so there is no reset method to keep in step with them and no list of classes
    /// to remember to clear. A new song is a new object.
    ///
    /// One thing does not start clean with it: the engine's input array, which belongs to the scene
    /// rather than to the chart. A lane the song before it was holding is still held, so the floor is
    /// handed that debt rather than a clean slate — see <see cref="Floor.HoldsAny"/>.
    ///
    /// The frame, once per `_VD._Oz` call, before the original body runs:
    ///
    /// <list type="number">
    /// <item><description>read the song clock, and notice a restart</description></item>
    /// <item><description>aim the sky cursor at the bar being held, and pin it to that value</description></item>
    /// <item><description>release and press the floor lanes, or let go of them if the mod is not
    /// driving them</description></item>
    /// <item><description>tally the verdicts of notes that have finished</description></item>
    /// </list>
    ///
    /// The sky bar pin is not here: it happens inside its own hook, because the state that matters for a
    /// bar is the one `_VD._Oz` reads a few instructions after `_VD._oz` returns, not the one at the top
    /// of the frame.
    /// </summary>
    internal sealed class Song
    {
        /// <summary>
        /// A drop in the song clock larger than this is a restart, not a slow frame.
        ///
        /// The pause menu moves the clock back too — one second, on resume, from wherever the song
        /// was — so this threshold on its own fires in the middle of a song that nobody restarted.
        /// <see cref="IsRestart"/> is what tells the two apart.
        /// </summary>
        private const double RewindMs = 500.0;

        /// <summary>
        /// A drop larger than this is a restart wherever it landed, because the resume cannot account
        /// for it: that one takes back exactly one second, and nothing else in the game moves the
        /// clock backwards.
        /// </summary>
        private const double RestartDropMs = 1500.0;

        /// <summary>
        /// The song in progress. Never null — before the first chart it owns nothing and the hooks find
        /// an empty chart, which is the same state a song with no notes is in.
        /// </summary>
        internal static Song Current { get; private set; } = new Song(IntPtr.Zero);

        /// <summary>The `LogicalNotePlayer` this song came from — a new one is a new song.</summary>
        private readonly IntPtr _owner;

        private double _lastMs = -1.0;

        internal Chart Chart { get; } = new Chart();
        internal Floor Floor { get; }
        internal Sky Sky { get; } = new Sky();
        internal SkyBar Bar { get; } = new SkyBar();
        internal Verdicts Verdicts { get; }

        /// <summary>Chart time at the last tick, in milliseconds; -1 before the first one.</summary>
        internal double NowMs { get; private set; } = -1.0;

        /// <summary>
        /// <paramref name="previous"/> is the floor of the song this one replaces, when it is a rebuild
        /// rather than the first song. The input array belongs to the engine and not to the chart, so a
        /// lane the song before it was holding is still held — see <see cref="Floor.HoldsAny"/>, which
        /// is where that is carried.
        /// </summary>
        internal Song(IntPtr owner, Floor previous = null)
        {
            _owner = owner;

            // What is left of the judgement point's state really is per-song even though the object
            // removal is not — see JudgementPoint, which keeps the rest of it on purpose.
            JudgementPoint.Reset();

            Floor = new Floor(Chart, previous);
            Verdicts = new Verdicts(Chart);
        }

        /// <summary>
        /// From the `LogicalNotePlayer._hb` detour. `_hb`'s first argument is the singleton, so it is
        /// also the stable way to notice that a new song has been loaded — which is a different
        /// instance, not a flag that gets cleared.
        /// </summary>
        internal static void Observe(IntPtr lnp, double chartSeconds)
        {
            if (lnp != Current._owner)
            {
                // Entering a chart is when the settings are re-read. Everything downstream reads
                // Config.Autoplay at the moment it needs it, so changing it here is all a change needs
                // — no game restart. Logged because "did the edit take?" is the question this answers.
                ConfigFile.Load();
                Diagnostics.Info($"chart start - {Config.Summary()}");

                Current = new Song(lnp, Current.Floor);
            }

            Current.Chart.TryLoad(lnp, chartSeconds * 1000.0);
        }

        /// <summary>From the `_VD._Oz` detour, before the original body runs.</summary>
        internal static void Frame(IntPtr engine, double seconds)
        {
            if (!Memory.LooksLikeObject(engine)) return;

            double nowMs = Memory.F64(engine + Offsets.Engine.ChartTime) * 1000.0;
            if (nowMs <= 0.0) return;

            double dropMs = Current._lastMs - nowMs;

            if (Current._lastMs >= 0.0 && dropMs > RewindMs && IsRestart(Current.Chart, dropMs, nowMs))
            {
                // A restart counts as entering the chart again, so the settings are re-read here too:
                // pause, edit the file, restart, and the change is in.
                ConfigFile.Load();
                Diagnostics.Info($"song clock rewound to {nowMs:F0}ms; restarting - {Config.Summary()}");

                Current = new Song(Current._owner, Current.Floor);
            }

            Song song = Current;
            song._lastMs = nowMs;
            song.NowMs = nowMs;

            // Writing the input is the only part autoplay=0 switches off, and the one write that does
            // not stop with it is letting go of the lanes a song was already being played with.
            // Reading the chart and tallying the game's own grades keeps running either way, which is
            // what makes observe mode worth having.
            if (Hooks.Writing) song.Play(engine, seconds, nowMs);
            else song.LetGoLanes(engine, seconds);

            song.Verdicts.Tick(nowMs);

#if DEBUG
            if (Config.DryRun) Report.DryRunTrace();
#endif
        }

        /// <summary>
        /// Whether a drop in the clock is the chart starting over rather than the pause menu coming
        /// back. Two ways to tell, and both are needed:
        ///
        /// <list type="bullet">
        /// <item><description>a drop past <see cref="RestartDropMs"/> is a restart wherever it landed:
        /// the resume takes back one second and nothing else moves the clock backwards;</description></item>
        /// <item><description>a drop that small is a restart only when the clock landed in the chart's
        /// opening — at or before its first note, or within the half second
        /// <see cref="RewindMs"/> the clock has to reach to be read at all.</description></item>
        /// </list>
        ///
        /// Both clauses are needed, and the first is not the redundant one: <see cref="Frame"/> returns
        /// before it reads a clock at or below zero, so on a chart whose first note sits at time zero a
        /// restart's landing compares as *after* that note — the second clause cannot see the one case
        /// it was written for. The first clause catches it for any restart more than a second and a half
        /// into the song, and the half second in the second clause catches the rest.
        ///
        /// Both err towards calling it a restart, because only one of the two mistakes is expensive.
        /// A restart that goes unnoticed leaves `Floor._pressed` holding every note id of the attempt
        /// before it — the same ids, since it is the same chart — and `Floor`'s cursor parked past the
        /// last note, so the second attempt plays nothing at all and the game grades it Miss end to
        /// end. A resume read as a restart costs one chart read and a frame or two of press latency.
        ///
        /// The case a resume does get read as a restart is a pause early in a song: within a second of
        /// the chart's first note, or before the clock reaches half a second. That is the only place a
        /// one-second rewind lands inside the opening. A chart that has not been read yet counts as a
        /// restart too — there is nothing in it for a rebuild to lose, and a restart in the frames
        /// before the chart arrives would otherwise be missed outright.
        /// </summary>
        private static bool IsRestart(Chart chart, double dropMs, double nowMs)
        {
            if (dropMs > RestartDropMs) return true;
            if (!chart.Loaded) return true;

            // Loaded means one of the two readers produced this chart, and neither of them returns one
            // it read no notes into — so there is a first note to compare against.
            double opening = Math.Max(chart.Notes[0].StartMs, RewindMs);
            return nowMs <= opening;
        }

        /// <summary>
        /// Called from the `_Oz` detour on the frames after a fault, when <see cref="Frame"/> is not
        /// reached any more. A fault switches the mod off; it does not undo what the mod already
        /// wrote, and a lane left held goes on grading the game's hold notes — see
        /// <see cref="Floor.LetGo"/>.
        /// </summary>
        internal static void LetGo(IntPtr engine, double seconds) => Current.LetGoLanes(engine, seconds);

        /// <summary>
        /// Hands the floor lanes back for as long as the mod is not the one playing them, which is
        /// either the switch being off or a detour having faulted.
        ///
        /// Asked every frame and answered from the schedule — plus, until the first pass has swept
        /// them, from the lanes the song before this one was holding — so all but the first of those
        /// frames stop at a comparison per lane and touch nothing.
        /// </summary>
        private void LetGoLanes(IntPtr engine, double seconds)
        {
            if (!Floor.HoldsAny) return;

            IntPtr lanes = Memory.InputLanes(engine);
            if (lanes != IntPtr.Zero) Floor.LetGo(lanes, seconds);
        }

        private void Play(IntPtr engine, double seconds, double nowMs)
        {
            // Both input writers have already run by now — `_Pz` and `_Qz` each write their press or
            // their cursor and only then call `_Oz` — so this is the last point before the game reads,
            // and therefore the place to undo them.
            if (Config.Sky)
            {
                Sky.AimAtBar(Chart, nowMs);
                Sky.Hold(engine);
            }

            IntPtr lanes = Memory.InputLanes(engine);
            if (lanes != IntPtr.Zero) Floor.Tick(lanes, seconds, nowMs);
        }
    }
}
