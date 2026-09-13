using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MelonLoader.NativeUtils;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// The `UiSettingGameplayPanel.Awake` detour — debug builds only.
    ///
    /// `Awake` is the moment the page's rows exist and are bound, so it is the one point at which the
    /// row this mod borrows can be read in its freshly constructed state. That is worth a log line when
    /// a take-over misbehaves and worth nothing otherwise, which is why the whole hook — not just its
    /// body — is in the folder `Source\Probe` that a Release build does not compile. A Release build
    /// does not patch this function at all, and carries neither the detour nor the probe it feeds.
    ///
    /// It lives here rather than in `Interop\Hooking\Detours` with the other eight for that reason: that
    /// folder is one file per function the mod actually patches, and a Release build patches this one
    /// never. `Hooks.Install` still decides whether to install it, and `Hooks.Uninstall` names it under
    /// the same guard.
    /// </summary>
    internal static unsafe partial class Hooks
    {
        /// <summary>private void Awake() — the "游戏和UI" page.</summary>
        private const long RvaSettingsAwake = 0x6BD700;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AwakeFn(IntPtr self);

        private static NativeHook<AwakeFn> _settingsAwake;
        private static AwakeFn _settingsAwakeTramp;

        internal static bool InstallSettingsAwake()
        {
            IntPtr target = MethodResolver.ByName("UiSettingGameplayPanel", "Awake", RvaSettingsAwake);
            if (target == IntPtr.Zero) return false;

            _settingsAwake = new NativeHook<AwakeFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&SettingsAwakeDetour,
            };
            _settingsAwake.Attach();
            _settingsAwakeTramp = _settingsAwake.Trampoline;
            Diagnostics.Info("UiSettingGameplayPanel.Awake hooked");
            return true;
        }

        internal static void DetachSettingsAwake()
        {
            _settingsAwake?.Detach();
            _settingsAwake = null;
            _settingsAwakeTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void SettingsAwakeDetour(IntPtr self)
        {
            _settingsAwakeTramp(self);

            try
            {
                if (Config.SettingsRow && Memory.LooksLikeObject(self))
                    SettingsProbe.RowState(self, "awake");
            }
            catch (Exception e)
            {
                // Not Fault(): a settings screen that will not take a row is not a reason to stop
                // playing the chart.
                Diagnostics.Warn($"AUTO row: awake hook failed: {Diagnostics.Describe(e)}");
            }
        }
    }
}
