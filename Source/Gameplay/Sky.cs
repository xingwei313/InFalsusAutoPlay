using System;
using System.Collections.Generic;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// The sky cursor value — `_VD+0x10` and its clamped copy at `_VD+0x98` — and the aim that moves
    /// it.
    ///
    /// Two things move the value, both of them onto a note being played: <see cref="AimAt"/> onto a
    /// flick being graded, and <see cref="AimAtBar"/> onto the bar currently being held. This class
    /// is what keeps it there: a mouse moved mid-song writes the cursor through `_VD._Mz` on every
    /// mouse event, and `_Qz` does that after it has called `_Oz`, so there is no window in which the
    /// game is guaranteed to see autoplay's value rather than the player's. Rewriting it once per
    /// tick, before `_Oz` reads, is what makes the mouse inert.
    ///
    /// The cursor is not judgement-neutral. A flick's grade does not read it — `_VD._nz` computes
    /// that from timing, and the hook forces the `cursorOnNote` argument that would otherwise gate
    /// it — but two other things do:
    ///
    /// <list type="bullet">
    /// <item><description>`_VD._nz` spawns a flick's hit effect at the cursor for anything but a
    /// Miss, and `Track._JBA` starts a burst that keeps re-reading the cursor every frame, so the
    /// effect stays where the cursor goes for as long as that animation runs.</description></item>
    /// <item><description>`_VD._Oz` only opens a new group of a sky bar while the cursor is inside
    /// that bar, and opening a group is what enqueues the bar's hit sound. A bar the cursor is not
    /// on is graded correctly and played silently.</description></item>
    /// </list>
    ///
    /// Both of those want the cursor on the note being played at that moment, which is why it is
    /// aimed at the notes rather than parked. What pinning still buys is the judgement point: it
    /// stops a moved mouse dragging that around and re-showing the ghost cursor behind
    /// <see cref="JudgementPoint.KeepOff"/>'s back.
    ///
    /// <para>Sky flicks (type 4) are graded by `_VD._nz` from timing alone:</para>
    ///
    ///     if ( fabs(delta) &lt;= 50.0 )       grade = 6;      // Perfect
    ///     else if ( fabs(delta) &lt;= 100.0 ) grade = (delta &lt;= 0) + 4;
    ///     else if ( fabs(delta) &lt;= 200.0 ) grade = (delta &lt;= 0) + 2;
    ///     else                             grade = (delta &lt; -200.0);
    ///
    /// but it only commits that grade when its fourth argument, `cursorOnNote`, is set:
    ///
    ///     if ( (mask &amp; allowed) != 0 &amp;&amp; (cursorOnNote || grade == 1) ) group.grade = grade;
    ///
    /// The game passes 1 only when the mouse has just dragged the cursor across the note — it gates
    /// the flag on two `_TD` trackers that only the mouse handler feeds. With no hand on the mouse it
    /// is always 0, and the note is never graded at all. Forcing it to 1 does not fake a grade: the
    /// grade is still computed by the game from how close the tick landed, and a tick at 60 fps is
    /// always inside the 50 ms Perfect window. The forcing itself is in the hook — see
    /// Detours/SkyFlickHook.cs.
    ///
    /// The aim is always taken from the note being graded, never from a guess about which note is
    /// next: the effect is drawn where the cursor is, so a guess that drifts by one note lands the
    /// effect in the wrong place.
    /// </summary>
    internal sealed class Sky
    {
        /// <summary>The X autoplay keeps the cursor at. Moved only by the two aims.</summary>
        private float _x;

        private bool _held;

        /// <summary>The chart's sky bars, rebuilt whenever a new chart is published.</summary>
        private ChartNote[] _bars = Array.Empty<ChartNote>();
        private int _revision = -1;

        // Read by the status line only.
        internal long Aims;
        internal float LastAim = -1f;

        /// <summary>
        /// Aims the cursor at the sky bar being held, called once per tick before <see cref="Hold"/>
        /// writes it.
        ///
        /// `_VD._Oz` opens a bar's next group only while the clamped cursor is inside that bar's
        /// edges — `left - 0.02 .. right + 0.02`, against the same edges <see cref="BarGeometry"/>
        /// computes — and opening the group is what advances `_Ae._qfA`, which is the only thing that
        /// makes `_Oz` enqueue the bar's hit sound at the end of the tick. Left unaimed the cursor
        /// sits wherever the last flick put it, so every bar that does not happen to cross that one
        /// value is graded correctly, silently, and with no hit sound.
        ///
        /// The centre is inside the bar by construction. When no bar is live the cursor is left
        /// alone: a flick being graded will have moved it, and a passage with neither keeps the value
        /// it had.
        /// </summary>
        internal void AimAtBar(Chart chart, double nowMs)
        {
            if (Config.DryRun) return;

            if (chart.Revision != _revision)
            {
                _revision = chart.Revision;
                _bars = Bars(chart.Notes);
            }

            for (int i = 0; i < _bars.Length; i++)
            {
                ChartNote bar = _bars[i];

                // Ordered by start time, so once one starts after now, so does every one behind it.
                if (bar.StartMs > nowMs) return;
                if (bar.EndMs < nowMs) continue;

                float x = BarGeometry.CurrentCentreX(bar, nowMs);
                if (float.IsNaN(x)) return;

                LastAim = Clamp(x);
                _x = LastAim;
                _held = true;
                return;
            }
        }

        private static ChartNote[] Bars(ChartNote[] notes)
        {
            var bars = new List<ChartNote>();
            foreach (ChartNote n in notes)
            {
                if (n.IsSky && n.Type == Offsets.NoteType.SkyBar) bars.Add(n);
            }
            return bars.ToArray();
        }

        /// <summary>
        /// Pins the sky cursor to the value autoplay maintains, called once per tick before the game
        /// reads it — see the class summary for why this has to happen here and not at the point of
        /// the write.
        /// </summary>
        internal void Hold(IntPtr engine)
        {
            if (Config.DryRun) return;
            if (!Memory.LooksLikeObject(engine)) return;

            if (!_held)
            {
                // Clamped on the way in: pinning a value outside 0..1 would make `Track._FBA` treat
                // the cursor as off-strip for the rest of the song and show the ghost every frame.
                _x = Clamp(Memory.F32(engine + Offsets.Engine.CursorRaw));
                _held = true;
            }

            Write(engine);
        }

        /// <summary>
        /// Called from the `_VD._nz` detour, before the original runs, for the note about to be graded.
        ///
        /// This also moves the value <see cref="Hold"/> pins, so the cursor stays on the note it was
        /// aimed at rather than snapping back to wherever it started.
        ///
        /// Only notes inside the window `_VD._nz` will accept are aimed at. Past
        /// <see cref="JudgementWindowMs"/> `_nz` returns without grading anything — and it is still
        /// called for every unjudged flick on the plane on every tick, so aiming at those parks the
        /// cursor on a note nothing is about to happen to. A burst reads the cursor every frame for as
        /// long as it animates, which is what turns a parked cursor into a hit effect that has slid
        /// off the note it belongs to.
        /// </summary>
        internal void AimAt(IntPtr engine, IntPtr note)
        {
            if (Config.DryRun) return;
            // LooksLikeNote, not LooksLikeObject: `note` is an interior pointer into the note array,
            // so its first field is the note id rather than a klass pointer.
            if (!Memory.LooksLikeObject(engine) || !Memory.LooksLikeNote(note)) return;

            // `_nz` opens with the same test, and grades nothing past it.
            if (Memory.F64(note + Offsets.Note.DeltaMs) > JudgementWindowMs) return;

            float x = BarGeometry.CurrentCentreX(note);
            if (float.IsNaN(x)) return;

            float clamped = Clamp(x);

            LastAim = clamped;
            Aims++;

            _x = clamped;
            _held = true;
            Write(engine);
        }

        /// <summary>
        /// How far ahead of its judgement moment a note may be aimed at — `_VD._nz`'s own
        /// `if (delta > 200.0) return`, past which it grades nothing.
        /// </summary>
        private const double JudgementWindowMs = 200.0;

        /// <summary>
        /// The raw value sits outside 0..1 whenever the mouse is off the end of the strip, and the
        /// game keeps the clamped copy at `_VD+0x98` for that reason; every aim here wants the
        /// clamped one.
        /// </summary>
        private static float Clamp(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);

        /// <summary>
        /// Both copies, always. `_Oz` scores its sky bars against the clamped one at +0x98 and `_nz`
        /// reads the raw one at +0x10, and the game's own mouse handler recomputes one from the
        /// other — leaving either behind would let them drift apart.
        /// </summary>
        private void Write(IntPtr engine)
        {
            Memory.WriteF32(engine + Offsets.Engine.CursorRaw, _x);
            Memory.WriteF32(engine + Offsets.Engine.CursorClamped, _x);
        }
    }
}
