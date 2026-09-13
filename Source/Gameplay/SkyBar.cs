using System;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// Sky bars (type 5) — the sky notes that are graded inline in `_VD._Oz` rather than in any
    /// function that can be hooked.
    ///
    /// The grade turns on two fields of the embedded `_Ae` struct at `_VD+0x18`:
    ///
    ///     if ( _Ae._pfA &gt; 0.0 || (_Ae._tfA == 0 &amp;&amp; duration + delta &lt; 0.0) ) grade = 1;  // Miss
    ///     else { if ( _Ae._tfA == 0 ) break;  grade = 6; }                               // Perfect
    ///
    /// so a bar is Perfect exactly when `_pfA &lt;= 0` and `_tfA != 0`. `_tfA == 0` is worse than it
    /// looks: the `break` skips every remaining tick of every remaining note in the loop, so the bar
    /// is not judged Miss — it is not judged at all until the bar has ended, and then the whole bar
    /// turns Miss at once.
    ///
    /// Both flags are maintained by `_VD._oz`, a small rate-limited timer that `_Oz` calls:
    ///
    /// <list type="bullet">
    /// <item><description>`_oz` drains `_OfA` (safe duration, 300 max) and, when it reaches zero,
    /// does `_Ae._tfA = 0; _Ae._pfA = 500.0;` — a forced Miss. `_OfA` only refills while a note is
    /// inside its judgement window, and a bar spends most of its length outside one, so this fires
    /// constantly. Worse, it also closes `_Ae._JZ()` (`OfA &gt; 0 &amp;&amp; pfA &lt;= 0 &amp;&amp; sfA`),
    /// which is the gate on the second path that could set `_tfA` again — so once it collapses it
    /// cannot recover.</description></item>
    /// <item><description>`_tfA` itself is otherwise set by a one-shot sample of where the cursor
    /// was when the bar opened, and by a sliding scan along the bar.</description></item>
    /// </list>
    ///
    /// Both of those want the cursor on the bar — which does not have to be arranged, because `_oz`
    /// is called immediately before the grade is decided, once per pending tick. Pinning the three
    /// fields in a hook on `_oz` means the game's own check reads them as "a player who never left
    /// the bar", so no cursor has to be steered and no one-shot sample has to land.
    ///
    /// That is the whole sky bar fix. The bar's own hit effect is spawned from the bar's geometry
    /// (`_HA._Yb`, the same function that positions it), not from the cursor, so nothing else is
    /// needed for it either.
    ///
    /// `_OfA`, `_pfA` and `_tfA` are read and written only inside `_VD._Oz` and `_VD._oz` — checked
    /// by scanning every instruction in the binary that touches `_VD+0x18`, `+0x20` and `+0x52` with
    /// `_VD` as the base. `_VD._Iz()`, the only accessor that hands the struct out, has no callers.
    /// Track reads none of it: the safe-area visuals are driven from `Track._gBA`, which does not even
    /// receive the engine. So this cannot make the judgement disagree with the picture.
    /// </summary>
    internal sealed class SkyBar
    {
        // Read by the status line only.
        internal long Pins;

        /// <summary>
        /// Called from the `_VD._oz` detour, after the original has run.
        ///
        /// `_Oz` calls `_oz` on the line immediately above the sky bar grade check, so this is the
        /// last thing to touch the state before the game reads it. `_oz` is the only thing in the
        /// binary that can set `_tfA = 0` or raise `_pfA`, so re-pinning after it is enough on its
        /// own — no cursor, no scheduling, no per-frame scan.
        ///
        /// This is the one write in the mod that changes what the game grades. It is not a faked
        /// verdict: it is the state a player who never left the bar would have, and the grade that
        /// follows is still the game's own, computed from the tick's own timing.
        /// </summary>
        internal void Pin(IntPtr engine)
        {
            if (Config.DryRun) return;
            if (!Memory.LooksLikeObject(engine)) return;

            Memory.WriteF64(engine + Offsets.SafeArea.SafeDuration, Offsets.SafeArea.SafeDurationFull);
            Memory.WriteF64(engine + Offsets.SafeArea.ForcedMiss, 0.0);
            Memory.WriteU8(engine + Offsets.SafeArea.StartOk, 1);
            Pins++;
        }
    }
}
