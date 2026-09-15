using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MelonLoader.NativeUtils;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// The hook on the coroutine that performs a scene change, which is where a song's end is turned
    /// away from the results screen — see <see cref="ResultsSkip"/>, which holds the whole of the
    /// decision. This file is only the interception.
    /// </summary>
    internal static unsafe partial class Hooks
    {
        /// <summary>
        /// `CoreScene._bj(_AB, string, string, Action, Action&lt;_Gc&gt;, Action&lt;_Gc&gt;, Func&lt;bool&gt;)`
        /// — the coroutine that fades out, unloads one scene and loads another. Every scene change in
        /// the game goes through it, `_oH`/`_OH` included.
        /// </summary>
        private const long RvaCoreTransition = 0x6E3BE0;

        // The shape is the metadata's, and both ends of the call agree with it: `_Bj` sets up exactly
        // these eight slots before calling (this in rcx, the flags in edx, the two names in r8 and r9,
        // the four delegates as four stack arguments) and `_bj` reads exactly these eight (its state
        // object takes the enum, the owner, the two strings and the four delegates, and nothing else).
        // There is no trailing MethodInfo slot: the method is not generic, so no call site has one to
        // pass.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr TransitionFn(IntPtr self, int flags, IntPtr unloadName, IntPtr loadName,
                                             IntPtr onUnload, IntPtr onLoaded, IntPtr onLoadedSecond,
                                             IntPtr onReady);

        private static NativeHook<TransitionFn> _transition;
        private static TransitionFn _transitionTramp;

        /// <summary>
        /// Resident, and it has to be: what it rewrites is the transition that ends a song the mod
        /// played, and a song can end in the first minute of a session. There is no later point at
        /// which attaching it would still catch every end.
        /// </summary>
        private static bool InstallSceneTransition()
        {
            IntPtr target = MethodResolver.ByName("CoreScene", "_bj", RvaCoreTransition);
            if (target == IntPtr.Zero) return false;

            _transition = new NativeHook<TransitionFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, IntPtr, IntPtr, IntPtr,
                                                          IntPtr, IntPtr, IntPtr>)&SceneTransitionDetour,
            };
            _transition.Attach();
            _transitionTramp = _transition.Trampoline;
            Diagnostics.Info("CoreScene._bj hooked");
            return true;
        }

        private static void DetachSceneTransition()
        {
            _transition?.Detach();
            _transition = null;
            _transitionTramp = null;
        }

        /// <summary>
        /// Asks <see cref="ResultsSkip"/> whether this is the transition to rewrite, and forwards
        /// everything unchanged otherwise.
        ///
        /// The forwarding is argument for argument, and it has to be: this function performs every scene
        /// change in the game, so a changed argument on a transition the mod has no opinion about is a
        /// changed argument on the whole of the game's navigation.
        ///
        /// The question is wrapped, and an answer of "no" is what a failure produces — the play is
        /// recorded, which is what happens without the mod at all. It is deliberately not `Fault()`:
        /// that would switch autoplay off, and a scene change is where the mod is most likely to meet a
        /// scene it has not seen before. Nothing may throw across a native frame either way.
        /// </summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr SceneTransitionDetour(IntPtr self, int flags, IntPtr unloadName, IntPtr loadName,
                                                    IntPtr onUnload, IntPtr onLoaded, IntPtr onLoadedSecond,
                                                    IntPtr onReady)
        {
            try
            {
                if (ResultsSkip.Redirects(unloadName, loadName, out IntPtr destination))
                {
                    // The callbacks are dropped with the destination: the loaded-callback belongs to the
                    // scene that is no longer being loaded (it is what would call `ResultsScene`'s own
                    // methods), and the transition's state object null-checks all four, so a transition
                    // with none of them is a transition the game already knows how to run — which is the
                    // shape its own no-results modes ask for.
                    return _transitionTramp(self, flags, unloadName, destination,
                                            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch (Exception e)
            {
                ResultsSkip.NoteFailure(e);
            }

            return _transitionTramp(self, flags, unloadName, loadName,
                                    onUnload, onLoaded, onLoadedSecond, onReady);
        }
    }
}
