extern alias UnityEngineCore;

using System;
using MelonLoader;

[assembly: MelonInfo(typeof(InFalsusAutoPlay.AutoPlayMod), "InFalsusAutoPlay", "0.3.0", "infalsus", null)]
[assembly: MelonGame("lowiro", "infalsus")]

namespace InFalsusAutoPlay
{
    /// <summary>
    /// Entry point: load the config, install the hooks, and — in a Debug build — report on a timer what
    /// they have been doing.
    ///
    /// Everything it says goes through <see cref="Diagnostics"/>, which is conditional on DEBUG, so a
    /// Release build loads, installs, plays, and says nothing at all. That is deliberate: the reporting
    /// exists to diagnose the mod rather than to make it play, and `build.bat Debug` is the build that
    /// carries it.
    /// </summary>
    public class AutoPlayMod : MelonMod
    {
#if DEBUG
        private float _nextReport;
#endif

        public override void OnInitializeMelon()
        {
            Diagnostics.Load("InFalsusAutoPlay loading...");
        }

        // The interop assemblies are only wired up after MelonLoader has generated them, so the hooks go
        // in here rather than in OnInitializeMelon.
        public override void OnLateInitializeMelon()
        {
            try
            {
                ConfigFile.Initialize();

#if DEBUG
                Diagnostics.Info(Config.Summary());
                Diagnostics.Info($"config: {Config.Path} (re-read on entering a chart)");
#endif

                // Before anything reads game memory: ask the running game where its fields
                // actually are, rather than trusting the offsets this build was reversed with.
                Offsets.Resolve();
                SettingsOffsets.Resolve();

#if DEBUG
                Diagnostics.Load(FieldResolver.Stats());
#endif

                Hooks.Install();
            }
            catch (Exception e)
            {
                Diagnostics.Error($"hook installation failed: {Diagnostics.Describe(e)}");
            }
        }

        public override void OnUpdate()
        {
#if DEBUG
            _nextReport += UnityEngineCore::UnityEngine.Time.unscaledDeltaTime;
            if (_nextReport < Config.ReportSeconds) return;
            _nextReport = 0f;

            Diagnostics.Info(Report.Line());
#endif
        }

        /// <summary>
        /// The borrowed row's per-frame re-assert, on the best clock there is for it.
        ///
        /// The row has to be re-stated after the page has had its say. The page's own pass — the one
        /// that disables that row outside the title screen, which is why there is a row to borrow —
        /// would otherwise undo the re-assert a frame later and leave the switch dead. Unity's order is
        /// every `Update`, then every `LateUpdate`, then the draw, so this lands after the page's
        /// `Update` by construction. (Checked: `UiSettingGameplayPanel` has an `Update` and no
        /// `LateUpdate`, so nothing of the game's competes for this slot.)
        ///
        /// Doing this with a detour on that `Update` would work and costs ~400 ms of freeze per
        /// attach, paid on the frame the page opens (see <see cref="Hooks"/>). `LateUpdate` is free
        /// and strictly later, so there is nothing a detour would buy.
        /// </summary>
        public override void OnLateUpdate()
        {
            AssistRow.Reassert();
        }

        public override void OnDeinitializeMelon()
        {
#if DEBUG
            Diagnostics.Info($"final — {Report.Line()}");
#endif
            Hooks.Uninstall();
        }
    }
}
