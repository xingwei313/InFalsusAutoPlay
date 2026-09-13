using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MelonLoader.NativeUtils;

namespace InFalsusAutoPlay
{
    internal static unsafe partial class Hooks
    {
        /// <summary>public void _hb(double,double,double,Dictionary,double) — the per-frame note culler.</summary>
        private const long RvaHb = 0x52FC80;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void HbFn(IntPtr lnp, double chartTime, double trackTime, double unitsPerSecond,
                                   IntPtr laneLists, double window);

        private static NativeHook<HbFn> _hb;
        private static HbFn _hbTramp;

        private static bool InstallNotePlayer()
        {
            IntPtr target = MethodResolver.ByName("LogicalNotePlayer", "_hb", RvaHb);
            if (target == IntPtr.Zero) return false;

            _hb = new NativeHook<HbFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, double, double, double, IntPtr, double, void>)&HbDetour,
            };
            _hb.Attach();
            _hbTramp = _hb.Trampoline;
            Diagnostics.Info("LogicalNotePlayer._hb hooked");
            return true;
        }

        private static void DetachNotePlayer()
        {
            _hb?.Detach();
            _hb = null;
            _hbTramp = null;
        }

        /// <summary>
        /// Not here to do anything to the call — `_hb` is the once-per-frame note culler and its first
        /// argument is the LogicalNotePlayer singleton, which is the stable way to reach the loaded
        /// chart without depending on a static .data offset.
        /// </summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void HbDetour(IntPtr lnp, double chartTime, double trackTime, double unitsPerSecond,
                                     IntPtr laneLists, double window)
        {
            if (!Faulted)
            {
                try
                {
                    Song.Observe(lnp, chartTime);
                }
                catch (Exception e)
                {
                    Fault("_hb", e);
                }
            }

            _hbTramp(lnp, chartTime, trackTime, unitsPerSecond, laneLists, window);
        }
    }
}
