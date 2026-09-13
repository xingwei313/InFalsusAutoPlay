namespace InFalsusAutoPlay
{
    /// <summary>
    /// What the mod can be told to do, and the constants that decide how it does it.
    ///
    /// Exactly one thing is settable: <see cref="Autoplay"/>, in a plain text file next to
    /// MelonLoader's own. The game's userV2.prefs is not an option — its keys are static readonly
    /// strings compiled into the game, and a mod cannot add one. Everything else is a constant.
    ///
    /// A const is a decision that has been made; the setting is the one thing that is the user's. A
    /// new setting's default answer is no: unless there is a nameable situation in which someone would
    /// want to change it, it is a const.
    /// </summary>
    internal static class Config
    {
        // ---------------------------------------------------------------- the setting

        /// <summary>
        /// Play the chart, or only watch it.
        ///
        /// Off means observe only: the mod still reads the chart and reports every grade the game
        /// produced, but writes nothing — no lane presses and no sky pins. That is the mode for
        /// checking the offsets against a human's play.
        /// </summary>
        internal static bool Autoplay = true;

        // ---------------------------------------------------------------- where it lives

        /// <summary>The settings file. Read at startup and again whenever a chart is entered.</summary>
        internal static readonly string Path =
            System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory,
                                   "InFalsusAutoPlay.cfg");

        // ---------------------------------------------------------------- the fixed decisions

        // The two modes are `static readonly` where the rest are `const`, and the difference is not
        // cosmetic: a const is folded, so the branch it turns off becomes unreachable code the
        // compiler reports. Feature switches survive that because their guards are written so the
        // taken branch is the foldable one; these two do not — dry-run's guards are early returns at
        // the top of five methods, and folding them makes each return unreachable (CS0162).

        /// <summary>Work out the input but do not write it. The A/B switch of last resort.</summary>
        internal static readonly bool DryRun = false;

        /// <summary>One line per finished note. Diagnostic, and only reachable from a Debug build.</summary>
        internal static readonly bool PerNoteLog = false;

        /// <summary>The three keyboard planes. Off leaves the input array alone entirely.</summary>
        internal const bool Floors = true;

        /// <summary>Both sky mechanisms: the flick gate and the bar pin.</summary>
        internal const bool Sky = true;

        /// <summary>
        /// The sky judgement point is not loaded while the mod drives that plane, because the mod is
        /// the one moving the cursor and the sprite would only be in the way. See
        /// <see cref="JudgementPoint"/>, which is also what puts it back if this is ever false.
        /// </summary>
        internal const bool HideCursor = true;

        /// <summary>
        /// The AUTO switch on the settings page. Off leaves that screen untouched: no AUTO row, and
        /// the `_WF` hook that would let the page reach the mod is not installed at all.
        /// </summary>
        internal const bool SettingsRow = true;

        // ---------------------------------------------------------------- the tuning

        /// <summary>
        /// How early to press a floor note, in milliseconds. Half a frame at 60 fps is about 8, which
        /// centres the press in the +/-25 ms Perfect window at either frame boundary.
        ///
        /// Do not raise this much: the game grades a press that lands 100-150 ms before its note as
        /// an immediate Miss, so a lead near 100 is worse than a lead of 0.
        /// </summary>
        internal const double PressLeadMs = 8.0;

        /// <summary>How long a tap's lane stays held after its note, so the hit registers.</summary>
        internal const double TapHoldMs = 50.0;

        /// <summary>How long past its end a hold's lane stays held.</summary>
        internal const double HoldSlackMs = 40.0;

        /// <summary>Seconds between status lines.</summary>
        internal const float ReportSeconds = 10f;

        // ---------------------------------------------------------------- reporting

        /// <summary>
        /// One line saying what governs a run — the one setting and the timings that shape the input
        /// it drives. The constants are deliberately not in it: each has one reachable value, so
        /// naming them would print the same thing on every run of every build.
        ///
        /// The text is Debug-only, but the method has to exist in a Release build: `[Conditional]`
        /// removes a log call without sparing the compiler from having to make sense of what was
        /// being logged, so a method named inside one of those arguments must still resolve.
        /// </summary>
        internal static string Summary()
        {
#if DEBUG
            return $"autoplay={(Autoplay ? "on" : "off")} " +
                   $"presslead={PressLeadMs:F0}ms taphold={TapHoldMs:F0}ms holdslack={HoldSlackMs:F0}ms";
#else
            return "";
#endif
        }
    }
}
