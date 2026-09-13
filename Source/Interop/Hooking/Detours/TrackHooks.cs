using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MelonLoader.NativeUtils;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// The two `Track` hooks, in one file because they are one feature seen from two sides:
    /// <see cref="JudgementPoint"/> is removed on enable and kept removed on every frame.
    ///
    /// A note on what they are not: neither of them is how the sky plane is played. They only stop
    /// the judgement point being drawn, which is worth nothing to the judgement and everything to
    /// looking at the screen.
    /// </summary>
    internal static unsafe partial class Hooks
    {
        /// <summary>`Track.OnEnable` — a four-instruction forwarder to `Track._ABA`.</summary>
        private const long RvaTrackOnEnable = 0x66EBC0;

        private const long RvaTrackUpdate = 0x66EBD0;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void TrackFn(IntPtr self);

        private static NativeHook<TrackFn> _trackOnEnable;
        private static TrackFn _trackOnEnableTramp;

        private static NativeHook<TrackFn> _trackUpdate;
        private static TrackFn _trackUpdateTramp;

        /// <summary>
        /// `Track.OnEnable` forwards to `Track._ABA`, which is where the track caches its materials
        /// and geometry. Hooking it is how the judgement point gets removed before the frame it would
        /// first have been drawn in — see <see cref="JudgementPoint.SuppressAt"/>.
        /// </summary>
        private static bool InstallTrackEnable()
        {
            IntPtr target = MethodResolver.ByName("Track", "OnEnable", RvaTrackOnEnable);
            if (target == IntPtr.Zero) return false;

            _trackOnEnable = new NativeHook<TrackFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&TrackOnEnableDetour,
            };
            _trackOnEnable.Attach();
            _trackOnEnableTramp = _trackOnEnable.Trampoline;
            Diagnostics.Info("Track.OnEnable hooked");
            return true;
        }

        private static void DetachTrackEnable()
        {
            _trackOnEnable?.Detach();
            _trackOnEnable = null;
            _trackOnEnableTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void TrackOnEnableDetour(IntPtr self)
        {
            _trackOnEnableTramp(self);

            TrackEnables++;

            try
            {
                // `OnEnable` is the first thing to run in the gameplay scene, which makes it the
                // earliest point at which "a chart is starting" is knowable — earlier than `_hb` first
                // sees the new player, and therefore earlier than the reload in Song.Observe. Reading
                // the file here is what stops the decision below being made on the previous song's
                // settings and the judgement point flashing out and back on the first frame.
                ConfigFile.Load();

                JudgementPoint.SuppressAt(self);
            }
            catch (Exception e)
            {
                JudgementPoint.NoteFailure(e);
            }
        }

        private static bool InstallTrackUpdate()
        {
            IntPtr target = MethodResolver.ByName("Track", "Update", RvaTrackUpdate);
            if (target == IntPtr.Zero) return false;

            _trackUpdate = new NativeHook<TrackFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&TrackUpdateDetour,
            };
            _trackUpdate.Attach();
            _trackUpdateTramp = _trackUpdate.Trampoline;
            Diagnostics.Info("Track.Update hooked");
            return true;
        }

        private static void DetachTrackUpdate()
        {
            _trackUpdate?.Detach();
            _trackUpdate = null;
            _trackUpdateTramp = null;
        }

        /// <summary>
        /// Runs the frame first, then puts the ghost cursor away if a real mouse pushed it out.
        /// `Track._FBA` re-enables the ghost from inside Update whenever the raw cursor value leaves
        /// 0..1, so acting before it would be undone immediately.
        /// </summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void TrackUpdateDetour(IntPtr self)
        {
            _trackUpdateTramp(self);

            try
            {
                JudgementPoint.KeepOff(self);
            }
            catch (Exception e)
            {
                // Not Fault(): a scene change must not switch autoplay off.
                JudgementPoint.NoteFailure(e);
            }
        }
    }
}
