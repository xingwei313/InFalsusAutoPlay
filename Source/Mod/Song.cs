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
    /// The frame, once per `_VD._Oz` call, before the original body runs:
    ///
    /// <list type="number">
    /// <item><description>read the song clock, and notice a rewind (restart or seek)</description></item>
    /// <item><description>pin the sky cursor back to the value autoplay owns</description></item>
    /// <item><description>release and press the floor lanes</description></item>
    /// <item><description>tally the verdicts of notes that have finished</description></item>
    /// </list>
    ///
    /// The sky bar pin is not here: it happens inside its own hook, because the state that matters for a
    /// bar is the one `_VD._Oz` reads a few instructions after `_VD._oz` returns, not the one at the top
    /// of the frame.
    /// </summary>
    internal sealed class Song
    {
        /// <summary>A drop in the song clock larger than this is a restart, not a slow frame.</summary>
        private const double RewindMs = 500.0;

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

        internal Song(IntPtr owner)
        {
            _owner = owner;

            // What is left of the judgement point's state really is per-song even though the object
            // removal is not — see JudgementPoint, which keeps the rest of it on purpose.
            JudgementPoint.Reset();

            Floor = new Floor(Chart);
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

                Current = new Song(lnp);
            }

            Current.Chart.TryLoad(lnp, chartSeconds * 1000.0);
        }

        /// <summary>From the `_VD._Oz` detour, before the original body runs.</summary>
        internal static void Frame(IntPtr engine, double seconds)
        {
            if (!Memory.LooksLikeObject(engine)) return;

            double nowMs = Memory.F64(engine + Offsets.Engine.ChartTime) * 1000.0;
            if (nowMs <= 0.0) return;

            if (Current._lastMs >= 0.0 && nowMs < Current._lastMs - RewindMs)
            {
                // A restart counts as entering the chart again, so the settings are re-read here too:
                // pause, edit the file, restart, and the change is in.
                ConfigFile.Load();
                Diagnostics.Info($"song clock rewound to {nowMs:F0}ms; restarting - {Config.Summary()}");

                Current = new Song(Current._owner);
            }

            Song song = Current;
            song._lastMs = nowMs;
            song.NowMs = nowMs;

            // Writing the input is the only part autoplay=0 switches off. Reading the chart and tallying
            // the game's own grades keeps running, which is what makes observe mode worth having.
            if (Config.Autoplay) song.Play(engine, seconds, nowMs);

            song.Verdicts.Tick(nowMs);

#if DEBUG
            if (Config.DryRun) Report.DryRunTrace();
#endif
        }

        private void Play(IntPtr engine, double seconds, double nowMs)
        {
            // Both input writers have already run by now — `_Pz` and `_Qz` each write their press or
            // their cursor and only then call `_Oz` — so this is the last point before the game reads,
            // and therefore the place to undo them.
            if (Config.Sky) Sky.Hold(engine);

            IntPtr lanes = Memory.InputLanes(engine);
            if (lanes != IntPtr.Zero) Floor.Tick(lanes, seconds, nowMs);
        }
    }
}
