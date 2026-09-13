using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MelonLoader.NativeUtils;

namespace InFalsusAutoPlay
{
    internal static unsafe partial class Hooks
    {
        /// <summary>private bool _nz(Dictionary, in _fA, bool, out _FH) — grades a sky flick.</summary>
        private const long RvaNz = 0x5F7A90;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte NzFn(IntPtr self, IntPtr noteDict, IntPtr note, byte cursorOnNote,
                                   IntPtr outGrade, IntPtr methodInfo);

        private static NativeHook<NzFn> _nz;
        private static NzFn _nzTramp;

        private static bool InstallSkyFlick()
        {
            IntPtr target = MethodResolver.ByName("_VD", "_nz", RvaNz);
            if (target == IntPtr.Zero) return false;

            _nz = new NativeHook<NzFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte, IntPtr, IntPtr, byte>)&NzDetour,
            };
            _nz.Attach();
            _nzTramp = _nz.Trampoline;
            Diagnostics.Info("_VD._nz hooked");
            return true;
        }

        private static void DetachSkyFlick()
        {
            _nz?.Detach();
            _nz = null;
            _nzTramp = null;
        }

        /// <summary>
        /// A sky flick is graded from timing, but only when `cursorOnNote` says the mouse happened to
        /// be dragging across the note; with no hand on the mouse it is never graded at all. Forcing
        /// it true does not fake anything — the grade still comes from how close the tick landed — and
        /// it is the same fix the sky plane needs, applied at the function that performs the grading
        /// rather than by moving a hidden cursor onto the note and hoping the one frame it is sampled
        /// on lands.
        /// </summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte NzDetour(IntPtr self, IntPtr noteDict, IntPtr note, byte cursorOnNote,
                                     IntPtr outGrade, IntPtr methodInfo)
        {
            if (Writing && Config.Sky)
            {
                SkyJudgements++;

                // The flag first: it is what lets the flick be graded at all, whereas AimAt only
                // decides where the hit effect is drawn. A failure in the cosmetic half must not cost
                // the judgement.
                cursorOnNote = 1;

                try
                {
                    Song.Current.Sky.AimAt(self, note);
                }
                catch (Exception e)
                {
                    Fault("_nz", e);
                }
            }

            return _nzTramp(self, noteDict, note, cursorOnNote, outGrade, methodInfo);
        }
    }
}
