using System;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// Where a note's own geometry puts it at a given moment. Only used to place a hit effect; no
    /// judgement consults it.
    /// </summary>
    internal static class BarGeometry
    {
        /// <summary>
        /// The note's centre X right now.
        ///
        /// This is `$HA.$Tb` / `_HA._Yb` transcribed rather than re-derived, because the obvious
        /// thing — interpolating startX towards endX — is wrong whenever the note changes width, and
        /// most sky bars do:
        ///
        ///     dLeft  = (endX - endHalfW) - (startX - startHalfW)
        ///     dRight = (endX + endHalfW) - (startX + startHalfW)
        ///     x0 = -startHalfW + startX + dLeft  * Ease(leftEase,  p)
        ///     x1 =  startHalfW + startX + dRight * Ease(rightEase, p)
        ///     centre = (x0 + x1) / 2
        ///
        /// Each edge carries its own easing. While the width is constant the two displacements are
        /// equal and the easings cancel out of the centre, so a plain lerp looks right; the moment
        /// the width changes they stop being equal and the centre walks away from a lerp. `_HA._Yb`'s
        /// decompilation decodes the same `_fA._he` bits the same way, which is what pins the
        /// selector down.
        /// </summary>
        internal static float CurrentCentreX(IntPtr note)
        {
            // `_fA._db()`, instruction for instruction:
            //     duration = _ce - _Be
            //     elapsed  = max(-_Ee, 0)
            //     progress = min(1, elapsed / duration)
            // _Ee (+0x50) is rewritten every frame by LogicalNotePlayer._hb, so this needs no clock
            // of its own and cannot drift from the time the game is judging against.
            double duration = Memory.I32(note + Offsets.Note.EndMs)
                            - Memory.I32(note + Offsets.Note.StartMs);
            if (duration <= 0.5) return Memory.F32(note + Offsets.Note.StartX);

            double delta = Memory.F64(note + Offsets.Note.DeltaMs);
            double elapsed = delta > 0.0 ? 0.0 : -delta;
            double progress = Math.Min(1.0, elapsed / duration);

            return Centre(note, progress);
        }

        private static float Centre(IntPtr note, double progress)
        {
            float startX = Memory.F32(note + Offsets.Note.StartX);
            float endX = Memory.F32(note + Offsets.Note.EndX);
            float startW = Memory.F32(note + Offsets.Note.StartWidth);
            float endW = Memory.F32(note + Offsets.Note.EndWidth);
            int flags = Memory.I32(note + Offsets.Note.Flags);

            float startHalf = startW * 0.5f;
            float endHalf = endW * 0.5f;

            float dLeft = (endX - endHalf) - (startX - startHalf);
            float dRight = (endX + endHalf) - (startX + startHalf);

            float x0 = -startHalf + startX + (float)(dLeft * Ease(LeftEase(flags), progress));
            float x1 = startHalf + startX + (float)(dRight * Ease(RightEase(flags), progress));

            return (x0 + x1) * 0.5f;
        }

        /// <summary>0 = linear, 1 = ease-in, 2 = ease-out. Bits 0x08/0x10 — `_HA._Yb`.</summary>
        private static int LeftEase(int flags) =>
            (flags & 0x08) != 0 ? 1 : ((flags & 0x10) != 0 ? 2 : 0);

        /// <summary>Same, for the right edge. Bits 0x40/0x80 — `_HA._Yb`.</summary>
        private static int RightEase(int flags) =>
            (flags & 0x40) != 0 ? 1 : ((flags & 0x80) != 0 ? 2 : 0);

        private static double Ease(int kind, double p)
        {
            const double HalfPi = Math.PI / 2.0;
            switch (kind)
            {
                case 1: return Math.Sin(p * HalfPi);        // $ie  ease-in
                case 2: return 1.0 - Math.Cos(p * HalfPi);  // $Ie  ease-out
                default: return p;                          // $He  linear
            }
        }
    }
}
