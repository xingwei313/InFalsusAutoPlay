using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MelonLoader.NativeUtils;

namespace InFalsusAutoPlay
{
    internal static unsafe partial class Hooks
    {
        /// <summary>private void _Oz(double, Queue&lt;_Sh&gt;) — the per-frame gameplay tick.</summary>
        private const long RvaOz = 0x5F4620;

        // ---------------------------------------------------------------- delegate shapes
        //
        // Taken from the Hex-Rays prototypes and the call sites, which is the only place the
        // stack-passed arguments and the trailing MethodInfo slot are visible — dump.cs understates
        // all of them.
        //
        // Argument positions decide the registers on Win64 (integer slots rcx/rdx/r8/r9, float slots
        // xmm0-xmm3, assigned by position rather than by "next free of that class"), which is why
        // `_Oz`'s double takes xmm1 and its Queue lands in r8 instead of rdx. The trailing IntPtr is
        // IL2CPP's MethodInfo slot: the call sites pass 0, so it is carried through unchanged rather
        // than omitted. Every other hook here is shaped the same way, for the same reason.

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void OzFn(IntPtr self, double time, IntPtr queue, IntPtr methodInfo);

        private static NativeHook<OzFn> _oz;
        private static OzFn _ozTramp;

        /// <summary>
        /// The frame. `_Oz` is the downstream end of input: `_Pz` and `_Qz` each write the input array
        /// and only then call it, so a detour that runs before the original body is the last place
        /// to put anything the game is about to read — see <see cref="Song.Frame"/>.
        /// </summary>
        private static bool InstallGameplayTick()
        {
            IntPtr target = MethodResolver.ByName("_VD", "_Oz", RvaOz);
            if (target == IntPtr.Zero) return false;

            _oz = new NativeHook<OzFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, double, IntPtr, IntPtr, void>)&OzDetour,
            };
            _oz.Attach();
            _ozTramp = _oz.Trampoline;
            Diagnostics.Info("_VD._Oz hooked");
            return true;
        }

        private static void DetachGameplayTick()
        {
            _oz?.Detach();
            _oz = null;
            _ozTramp = null;
        }

        // Nothing may throw across a native frame: an exception unwinding into IL2CPP code takes the
        // process down without a managed stack. Every detour is wrapped, and a fault disables the mod
        // instead of repeating every frame.

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OzDetour(IntPtr self, double time, IntPtr queue, IntPtr methodInfo)
        {
            if (!Faulted)
            {
                try
                {
                    Song.Frame(self, time);
                }
                catch (Exception e)
                {
                    Fault("_Oz", e);
                }
            }

            _ozTramp(self, time, queue, methodInfo);
        }
    }
}
