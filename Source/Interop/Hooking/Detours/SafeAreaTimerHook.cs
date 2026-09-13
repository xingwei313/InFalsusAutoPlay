using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MelonLoader.NativeUtils;

namespace InFalsusAutoPlay
{
    internal static unsafe partial class Hooks
    {
        /// <summary>
        /// `_VD._oz` — the safe-area timer, not to be confused with `_VD._Oz` (RVA 0x5F4620), the
        /// per-frame gameplay tick. The two differ by one letter and sit 13 KB apart: check which
        /// one a call site means before touching either.
        /// </summary>
        private const long RvaOzTimer = 0x5F7DE0;   // private void _oz(double, bool)

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void OzTimerFn(IntPtr self, double delta, byte inWindow, IntPtr methodInfo);

        private static NativeHook<OzTimerFn> _ozTimer;
        private static OzTimerFn _ozTimerTramp;

        /// <summary>
        /// The sky bar fix, and the only hook that changes a grade. `_Oz` calls this timer on the line
        /// directly above the sky bar grade check, so pinning the safe-area state on the way out is
        /// what the game reads on the way in — see <see cref="SkyBar.Pin"/>.
        /// </summary>
        private static bool InstallSafeAreaTimer()
        {
            IntPtr target = MethodResolver.ByName("_VD", "_oz", RvaOzTimer);
            if (target == IntPtr.Zero) return false;

            _ozTimer = new NativeHook<OzTimerFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, double, byte, IntPtr, void>)&OzTimerDetour,
            };
            _ozTimer.Attach();
            _ozTimerTramp = _ozTimer.Trampoline;
            Diagnostics.Info("_VD._oz (safe-area timer) hooked");
            return true;
        }

        private static void DetachSafeAreaTimer()
        {
            _ozTimer?.Detach();
            _ozTimer = null;
            _ozTimerTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OzTimerDetour(IntPtr self, double delta, byte inWindow, IntPtr methodInfo)
        {
            _ozTimerTramp(self, delta, inWindow, methodInfo);

            if (!Writing || !Config.Sky) return;

            try
            {
                Song.Current.Bar.Pin(self);
            }
            catch (Exception e)
            {
                Fault("_oz", e);
            }
        }
    }
}
