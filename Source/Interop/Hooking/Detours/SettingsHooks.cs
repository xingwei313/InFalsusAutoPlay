using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MelonLoader.NativeUtils;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// The two hooks on the settings page, in one file because they are two halves of one feature and
    /// have opposite lifetimes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `_WF` is resident, and it has to be. Its argument is the array of flags that decides which of
    /// the page's rows are in use — flag 46 is the assist row's — so the availability question is
    /// answered from the page's own decision rather than from how the row happens to look. Attach it
    /// late and the first call is missed, which is the one that decides. Hooking `_WF`'s caller
    /// instead is still a resident hook, with one more signature to get wrong.
    /// </para>
    /// <para>
    /// `_eg` is attached only while a row is borrowed. While the row belongs to the game there is no
    /// reason for a detour to sit on the page's own handler at all, and a detour that is not there
    /// cannot misbehave: the row is handed back complete, not handed back with the mod still standing
    /// in the way.
    /// </para>
    /// </remarks>
    internal static unsafe partial class Hooks
    {
        /// <summary>
        /// public void _WF(_Cb[]) — UiSettingGameplayPanel, called every time the "游戏和UI" tab is
        /// selected, with the flags that decide which of its rows are shown.
        /// </summary>
        private const long RvaSettingsShow = 0x6BF060;

        /// <summary>
        /// private void _eg(UiSettingsMultiselectorBar bar, int index) — the page's handler for the
        /// assist row's option buttons.
        ///
        /// Which handler belongs to which row is not written down anywhere the static database can be
        /// read from: the delegate the panel binds is built from a metadata token that only becomes a
        /// pointer at runtime. It is identified by its behaviour instead — it is the one that raises the
        /// confirmation dialog, which is exactly what pressing the assist row does.
        /// </summary>
        private const long RvaBarValueChanged = 0x6C0610;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ShowFn(IntPtr self, IntPtr flags);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void BarValueChangedFn(IntPtr self, IntPtr bar, int index);

        private static NativeHook<ShowFn> _settingsShow;
        private static ShowFn _settingsShowTramp;

        private static NativeHook<BarValueChangedFn> _barValueChanged;
        private static BarValueChangedFn _barValueChangedTramp;

        // ---------------------------------------------------------------- _WF

        /// <summary>
        /// Where the row is taken over and the config re-read.
        ///
        /// `_WF` does two things that both matter here: it applies the page's flags, and then it pushes
        /// the real settings into the rows. The first is the condition for touching the row at all; the
        /// second is why the mod's value has to be written after it rather than before.
        /// </summary>
        private static bool InstallSettingsShow()
        {
            IntPtr target = MethodResolver.ByName("UiSettingGameplayPanel", "_WF", RvaSettingsShow);
            if (target == IntPtr.Zero) return false;

            _settingsShow = new NativeHook<ShowFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&SettingsShowDetour,
            };
            _settingsShow.Attach();
            _settingsShowTramp = _settingsShow.Trampoline;
            Diagnostics.Info("UiSettingGameplayPanel._WF hooked");
            return true;
        }

        private static void DetachSettingsShow()
        {
            _settingsShow?.Detach();
            _settingsShow = null;
            _settingsShowTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void SettingsShowDetour(IntPtr self, IntPtr flags)
        {
            _settingsShowTramp(self, flags);

            try
            {
                AssistRow.SyncFromShow(self, flags);
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"AUTO row: show hook failed: {Diagnostics.Describe(e)}");
            }
        }

        // ---------------------------------------------------------------- _eg

        /// <summary>
        /// Where a press on the borrowed row is taken.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Installed and removed with the borrowing, not at startup. See the class summary.
        /// </para>
        /// <para>
        /// Do not rebind the row with `_yG` instead. `_yG` replaces three delegate fields, which
        /// leaves the game's own delegates with no reference at all — and copies kept on this side
        /// live in the managed heap, which il2cpp's conservative scan does not cover, so they can be
        /// collected while the row still points at them. That is a crash a few seconds after the
        /// click that first uses one.
        /// </para>
        /// <para>
        /// Intercepting here leaves the row's wiring completely alone: the game's own callback still
        /// runs, and the mod decides what it does.
        /// </para>
        /// </remarks>
        internal static bool AttachBarValueChanged()
        {
            if (_barValueChanged != null) return true;

            IntPtr target = MethodResolver.ByName("UiSettingGameplayPanel", "_eg", RvaBarValueChanged);
            if (target == IntPtr.Zero) return false;

            _barValueChanged = new NativeHook<BarValueChangedFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, void>)&BarValueChangedDetour,
            };
            _barValueChanged.Attach();
            _barValueChangedTramp = _barValueChanged.Trampoline;
            Diagnostics.Info("UiSettingGameplayPanel._eg (row option handler) hooked - while the " +
                             "row is borrowed only");
            return true;
        }

        internal static void DetachBarValueChanged()
        {
            if (_barValueChanged == null) return;

            _barValueChanged.Detach();
            _barValueChanged = null;
            _barValueChangedTramp = null;
            Diagnostics.Info("UiSettingGameplayPanel._eg unhooked - the page's handler is its own again");
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void BarValueChangedDetour(IntPtr self, IntPtr bar, int index)
        {
            try
            {
                // True when the press was on the borrowed row: the mod has dealt with it and the page's
                // own handling — which for this row is a confirmation dialog and then the assist
                // setting — is deliberately not reached.
                if (AssistRow.HandlePress(bar, index)) return;
            }
            catch (Exception e)
            {
                AssistRow.NotePressFailure(e);
            }

            _barValueChangedTramp(self, bar, index);
        }
    }
}
