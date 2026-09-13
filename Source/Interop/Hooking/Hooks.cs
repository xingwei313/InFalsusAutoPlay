using System;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// The native hooks: which game functions are patched, and what happens when one faults.
    ///
    /// All of them are thin. Each hook is a file of its own under Detours/ — its RVA, its delegate
    /// shape, its trampoline and its detour body — and the decisions live in <see cref="Song"/>,
    /// <see cref="AssistRow"/> and <see cref="JudgementPoint"/>, so a mistake here is a routing
    /// mistake rather than a gameplay one.
    ///
    /// RVAs are from Il2CppDumper's dump.cs; <see cref="MethodResolver"/> prefers the runtime
    /// MethodInfo and only falls back to them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two ways to be off, and why they are not one:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="Faulted"/> — a detour threw. Nothing this mod does is trustworthy after that, so
    /// every detour stops calling into the mod. It is deliberately not the same flag as the user's
    /// switch: a fault silently rewriting a setting is how a broken hook comes to look like a
    /// configuration mistake.
    /// </description></item>
    /// <item><description>
    /// <see cref="Writing"/> — the user's switch, plus no fault. This gates only the things that
    /// change what the game grades or shows. It does not gate the readback: with autoplay off the
    /// mod still reads the chart and reports every grade the game produced, which is what makes
    /// observe mode worth having.
    /// </description></item>
    /// </list>
    /// <para>
    /// Attaching a detour costs about 400 ms. Measured: eight hooks at startup, every one 395-418
    /// ms, independent of the size of the function being hooked — the shape of a fixed wait, not of
    /// work. Where it goes is not known; it is inside MelonLoader's own native bootstrap
    /// (`NativeHook<T>.HookAttach` → `BootstrapInterop.NativeHookAttach` →
    /// `NativeHookAttachDirect` → `BootstrapLibrary.NativeHookAttach`, with no source in this
    /// repository). It is not MonoMod — that is the Mono path and this game is IL2CPP.
    /// </para>
    /// <para>
    /// Two consequences worth carrying:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// "attach on demand" is not free. It moves a 400 ms freeze from loading, where nobody is
    /// looking, to the moment the player opens a menu. That is why only one settings hook is
    /// attached on demand and the rest are resident.
    /// </description></item>
    /// <item><description>
    /// `NativeHook<T>.Attach()` does not report failure —
    /// `CoreClrDelegateFixer.SanityCheckDetour` returns silently when it is not satisfied — so a
    /// "hooked" line in the log means the call was made, not that the hook is in place.
    /// </description></item>
    /// </list>
    /// </remarks>
    internal static unsafe partial class Hooks
    {
        /// <summary>Set when a detour throws. See the class summary for why this is not the setting.</summary>
        internal static bool Faulted { get; private set; }

        internal static int Faults { get; private set; }

        /// <summary>True when a detour should change what the game grades or shows.</summary>
        internal static bool Writing => Config.Autoplay && !Faulted;

        // Read by the status line only.
        internal static long SkyJudgements;
        internal static long TrackEnables;

        internal static void Install()
        {
            int installed = 0;
            int attempted = 0;

            // Attempts are counted beside the calls rather than written down as a total, so the
            // expected number cannot drift when one is added or removed.
            void Count(bool ok) { attempted++; if (ok) installed++; }

            // The order matters for one pair: `_hb` is what reads the chart, and `_Oz` is what
            // consumes it. Everything else is independent.
            Count(InstallGameplayTick());
            Count(InstallSkyFlick());
            Count(InstallSafeAreaTimer());
            Count(InstallNotePlayer());
            Count(InstallTrackUpdate());
            Count(InstallTrackEnable());

            // The settings page is the one part that can be left out entirely. That is a source-level
            // constant rather than a setting — see Config.SettingsRow. There is no message for the
            // off case: this gate is the only thing that has to know, because with `_WF` unhooked
            // nothing on the page can reach the mod at all, and the count below reads 6/6 either way.
            if (Config.SettingsRow)
            {
                // One of the settings hooks is resident, and only one. `_WF` carries the page's own
                // statement of which rows it is using, and nothing else carries it, so it has to be
                // in place before the page is first entered — there is no later point at which
                // attaching it would still catch the decision.
                //
                // The press handler (`_eg`) is attached only while a row is borrowed. The per-frame
                // re-assert is not a detour at all: it runs from the mod's own `OnLateUpdate`.
                Count(InstallSettingsShow());

#if DEBUG
                // `Awake` carries nothing but the page-construction probe, so a build with no probes
                // does not patch it — and does not compile the hook at all. See Probe/SettingsAwakeHook.cs.
                Count(InstallSettingsAwake());
#endif
            }

            Diagnostics.Info($"{installed}/{attempted} hooks installed");
            if (installed < attempted)
                Diagnostics.Warn("a hook is missing; whatever it drives will silently do nothing");
        }

        internal static void Uninstall()
        {
            DetachGameplayTick();
            DetachSkyFlick();
            DetachSafeAreaTimer();
            DetachNotePlayer();
            DetachTrackUpdate();
            DetachTrackEnable();
#if DEBUG
            DetachSettingsAwake();   // the hook itself is Debug-only; see Probe/SettingsAwakeHook.cs
#endif
            DetachSettingsShow();
            DetachBarValueChanged();
        }

        /// <summary>Reports a detour fault once, then switches the mod off rather than looping.</summary>
        private static void Fault(string where, Exception e)
        {
            Faulted = true;
            Faults++;

            if (Faults > 1) return;
            Diagnostics.Error($"{where} detour faulted, autoplay disabled: {Diagnostics.Describe(e)}");
        }
    }
}
