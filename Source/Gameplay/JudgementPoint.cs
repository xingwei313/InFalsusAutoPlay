extern alias UnityEngineCore;

using System;
using System.Collections.Generic;
using UnityEngineCore::UnityEngine;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// Removes the sky judgement point — the object a human drags along the strip with the mouse —
    /// so that it is never drawn.
    ///
    /// The point is four `[SerializeField]` references on `Track`, all of them pointing into the
    /// track scene:
    ///
    ///     +0x170  Transform      "cursor"                      the judgement point itself
    ///     +0x178  MeshRenderer   its renderer, cached apart
    ///     +0x1A0  Transform      "cursor_ghost"                shown while off the strip
    ///     +0x1A8  Transform      "SkyCursorExtrasContainer"    decoration that follows it
    ///
    /// They are switched off from a hook on `Track.OnEnable`, not from `Track.Update`. `OnEnable`
    /// runs after `Track.Awake` and `Track._ABA` have finished reading positions off these transforms
    /// and caching their materials, and it runs before the frame is drawn — so the point is never
    /// rendered once, not even for the first frame. Hiding it from `Update` instead means hiding
    /// something that has already been drawn, and undoing a decision the game has already made.
    ///
    /// Deactivated, never destroyed: `Track._bBA` and `Track._BBA` write `localPosition` into two of
    /// these transforms and are reached from the mouse handler, `Track._FBA` reads the ghost's
    /// `activeSelf` every frame, and `Track._ABA` reads the renderer. A destroyed object would leave
    /// all of that writing through a freed pointer, which is an access violation no try/catch can
    /// catch. Deactivating keeps everything valid and merely stops it being drawn.
    ///
    /// <para>Every other piece of gameplay state is per-song and is reset by being reconstructed.
    /// This one is deliberately not part of a <see cref="Song"/>, in both directions:</para>
    ///
    /// <list type="bullet">
    /// <item><description>`_partsAreHidden` must survive a song change. The config is re-read when
    /// the new chart is seen, which is after `Track.OnEnable` has already hidden the point under the
    /// previous settings, so the frame that should put it back is the one that would find the flag
    /// cleared. Leaving it set is safe because the restore re-reads the objects themselves rather
    /// than assuming which scene they came from.</description></item>
    /// <item><description>the counters deliberately survive too: how many times the point has been
    /// removed across the whole run is the number worth reporting, not the per-song
    /// one.</description></item>
    /// </list>
    /// </summary>
    internal static class JudgementPoint
    {
        /// <summary>
        /// True only while the mod is actually driving the sky plane.
        ///
        /// `HideCursor` on its own is not enough, and gating on it alone would be wrong: with
        /// `Autoplay` off, or with `Sky` off leaving the plane to the player, the judgement point is
        /// the thing they aim with and removing it makes the plane unplayable. It is pointless only
        /// when autoplay is moving the cursor itself — then nothing the player does with the mouse
        /// reaches it and there is nothing for it to show.
        /// </summary>
        private static bool ShouldHide => Config.Autoplay && Config.Sky && Config.HideCursor;

        /// <summary>
        /// The objects that make up the judgement point. The renderer is listed next to the transform
        /// it belongs to because this build caches them separately and they are not guaranteed to sit
        /// on the same GameObject.
        /// </summary>
        private static readonly int[] Parts =
        {
            Offsets.Track.SkyCursor,
            Offsets.Track.SkyCursorRenderer,
            Offsets.Track.SkyGhostCursor,
            Offsets.Track.CursorExtras,
        };

        private static long _suppressed;
        private static long _rescues;
        private static int _lastShape = -1;
        private static int _failures;

        /// <summary>
        /// Whether the parts are currently deactivated by the mod, so they can be brought back if the
        /// switches change mid-song. Only ever set for objects this mod switched off.
        /// </summary>
        private static bool _partsAreHidden;

        // Read by the status line only.
        internal static long Suppressed => _suppressed;
        internal static long Rescues => _rescues;

        /// <summary>
        /// Called from the `Track.OnEnable` detour, after the original — `Track._ABA` has to run
        /// first, because it caches a material off the cursor's renderer and that has to be a live
        /// renderer when it does.
        ///
        /// Doing this per enable rather than per frame also covers the Track being disabled and
        /// re-enabled, which would otherwise bring the point back.
        /// </summary>
        internal static void SuppressAt(IntPtr track)
        {
            if (!ShouldHide) return;
            if (!Memory.LooksLikeObject(track)) return;

            int hidden = 0;
            var seen = new List<IntPtr>(Parts.Length);

            foreach (int offset in Parts)
            {
                IntPtr component = Memory.Ptr(track + offset);
                if (!Memory.LooksLikeObject(component)) continue;

                GameObject go;
                try { go = new Component(component).gameObject; }
                catch (Exception e) { NoteFailure(e); continue; }

                if (go == null) continue;

                // The renderer usually shares its GameObject with the transform; switching the same
                // object off twice is harmless but would inflate the count.
                IntPtr handle = go.Pointer;
                if (seen.Contains(handle)) continue;
                seen.Add(handle);

                if (go.activeSelf) go.SetActive(false);
                hidden++;
            }

            _suppressed++;
            if (hidden > 0) _partsAreHidden = true;

            if (hidden != _lastShape)
            {
                _lastShape = hidden;
                Diagnostics.Info($"sky judgement point: {hidden}/{Parts.Length} objects " +
                                 "deactivated at Track.OnEnable");
            }
        }

        /// <summary>
        /// Puts back the parts this mod switched off.
        ///
        /// Needed because the gating happens in <see cref="SuppressAt"/>, which the game only calls
        /// on `Track.OnEnable` — and a restart does not re-enable the Track. Without this, flipping
        /// `autoplay` off and restarting the chart with the config re-read would leave the judgement
        /// point hidden and the sky plane unplayable.
        ///
        /// The ghost cursor is deliberately skipped: `Track._FBA` owns its active state and recomputes
        /// it every frame from the cursor value, so forcing it on would fight the game and it would be
        /// switched straight back.
        /// </summary>
        private static void Restore(IntPtr track)
        {
            if (!_partsAreHidden) return;
            _partsAreHidden = false;

            int restored = 0;
            foreach (int offset in Parts)
            {
                if (offset == Offsets.Track.SkyGhostCursor) continue;

                IntPtr component = Memory.Ptr(track + offset);
                if (!Memory.LooksLikeObject(component)) continue;

                try
                {
                    GameObject go = new Component(component).gameObject;
                    if (go == null || go.activeSelf) continue;

                    go.SetActive(true);
                    restored++;
                }
                catch (Exception e)
                {
                    NoteFailure(e);
                }
            }

            Diagnostics.Info($"sky judgement point: {restored} object(s) restored " +
                             "(the mod is no longer driving the sky plane)");
        }

        /// <summary>
        /// Called from the `Track.Update` detour, after the original.
        ///
        /// `Track._FBA` runs inside that original and switches the ghost cursor on whenever the raw
        /// cursor value leaves 0..1, so a mouse moved off the end of the strip puts the point back.
        /// This puts it away again, and pulls the value back onto the strip so the next frame's
        /// `_FBA` agrees.
        ///
        /// The float test is the whole point of doing it here: in a normal autoplay frame the value is
        /// already on the strip, nothing managed is touched, and the cost is one load and two compares.
        /// </summary>
        internal static void KeepOff(IntPtr track)
        {
            if (!Memory.LooksLikeObject(track)) return;

            // The switches can change mid-song — the config is re-read on a restart, and a restart
            // does not re-enable the Track — so OnEnable will not fire again to undo this. Put the
            // point back instead.
            if (!ShouldHide)
            {
                Restore(track);
                return;
            }

            IntPtr engine = Memory.Ptr(track + Offsets.Track.Engine);
            if (!Memory.LooksLikeObject(engine)) return;

            float raw = Memory.F32(engine + Offsets.Engine.CursorRaw);
            if (raw >= 0f && raw <= 1f) return;

            float clamped = raw < 0f ? 0f : 1f;
            Memory.WriteF32(engine + Offsets.Engine.CursorRaw, clamped);
            Memory.WriteF32(engine + Offsets.Engine.CursorClamped, clamped);

            IntPtr ghost = Memory.Ptr(track + Offsets.Track.SkyGhostCursor);
            if (!Memory.LooksLikeObject(ghost)) return;

            try
            {
                GameObject go = new Component(ghost).gameObject;
                if (go != null && go.activeSelf)
                {
                    go.SetActive(false);
                    _rescues++;
                }
            }
            catch (Exception e)
            {
                NoteFailure(e);
            }
        }

        /// <summary>Reports a fault without switching autoplay off — a scene change is expected.</summary>
        internal static void NoteFailure(Exception e)
        {
            _failures++;
            if (_failures <= 3)
                Diagnostics.Warn($"the sky judgement point could not be hidden ({_failures}): " +
                                 Diagnostics.Describe(e));
        }

        /// <summary>Per-song, and only the two that describe the shape of this scene.</summary>
        internal static void Reset()
        {
            _lastShape = -1;
            _failures = 0;
        }
    }
}
