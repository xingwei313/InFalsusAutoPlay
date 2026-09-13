using System;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// The sky cursor value — `_VD+0x10` and its clamped copy at `_VD+0x98` — and the flick aim that
    /// moves it.
    ///
    /// The value is moved by exactly one thing, <see cref="AimAt"/>, from the note being graded.
    /// This class is what keeps it there: a mouse moved mid-song writes the cursor through `_VD._Mz`
    /// on every mouse event, and `_Qz` does that after it has called `_Oz`, so there is no window
    /// in which the game is guaranteed to see autoplay's value rather than the player's. Rewriting it
    /// once per tick, before `_Oz` reads, is what makes the mouse inert.
    ///
    /// This is judgement-neutral either way — the sky plane is graded from `_Ae` and timing, not from
    /// the cursor — but it is what stops a moved mouse from dragging the judgement point's value
    /// around and re-showing the ghost cursor behind <see cref="JudgementPoint.KeepOff"/>'s back.
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
    /// `_nz` then spawns the hit effect, and for anything but a Miss it takes the position from
    /// the cursor, not from the note. So the cursor value still has to be right even though nothing
    /// is drawn there and no judgement reads it. It is computed from the note being graded — never
    /// from a guess about which note is next, which drifts and lands the effect in the wrong place.
    /// </summary>
    internal sealed class Sky
    {
        /// <summary>The X autoplay keeps the cursor at. Moved only by <see cref="AimAt"/>.</summary>
        private float _x;

        private bool _held;

        // Read by the status line only.
        internal long Aims;
        internal float LastAim = -1f;

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
                // Clamped on the way in: the raw value sits outside 0..1 whenever the mouse is off
                // the end of the strip, and pinning that would make `Track._FBA` treat the cursor as
                // off-strip for the rest of the song and show the ghost every frame.
                float initial = Memory.F32(engine + Offsets.Engine.CursorRaw);
                _x = initial < 0f ? 0f : (initial > 1f ? 1f : initial);
                _held = true;
            }

            Write(engine);
        }

        /// <summary>
        /// Called from the `_VD._nz` detour, before the original runs.
        ///
        /// This also moves the value <see cref="Hold"/> pins, so the cursor parks on the last flick
        /// judged rather than snapping back to wherever it started.
        /// </summary>
        internal void AimAt(IntPtr engine, IntPtr note)
        {
            if (Config.DryRun) return;
            // LooksLikeNote, not LooksLikeObject: `note` is an interior pointer into the note array,
            // so its first field is the note id rather than a klass pointer.
            if (!Memory.LooksLikeObject(engine) || !Memory.LooksLikeNote(note)) return;

            float x = BarGeometry.CurrentCentreX(note);
            if (float.IsNaN(x)) return;

            float clamped = x < 0f ? 0f : (x > 1f ? 1f : x);

            LastAim = clamped;
            Aims++;

            _x = clamped;
            _held = true;
            Write(engine);
        }

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
