namespace InFalsusAutoPlay
{
    /// <summary>
    /// The settings-screen types the AUTO row is built out of, and the addresses of the game methods
    /// that drive it.
    ///
    /// <para>
    /// These are separate from <see cref="Offsets"/> on purpose: that file describes the rhythm game,
    /// this one describes the settings UI, and nothing here is read during gameplay.
    /// </para>
    /// <para>
    /// Everything below was read out of the game's `GameAssembly.dll` — the field offsets from
    /// Il2CppDumper's `dump.cs` (TypeDefIndex 3386 / 3397 / 10981), the method behaviour from the
    /// Hex-Rays decompilation.
    /// </para>
    /// <para>
    /// The field offsets written here are the ones this build was reversed with; <see cref="Resolve"/>
    /// asks the running game for the real ones and overwrites them, so a game update that moves them
    /// does not have to be re-reversed. The RVAs need no such treatment — they are used with
    /// <see cref="MethodResolver"/>, which already prefers the runtime MethodInfo.
    /// </para>
    /// </summary>
    internal static class SettingsOffsets
    {
        // ---------------------------------------------------------------- UiSettingGameplayPanel
        //
        // The "游戏和UI" page. Its rows are [SerializeField] references baked into the prefab — there
        // is no list to append to, which is why the page's own row is borrowed rather than a new one
        // built. Only the row that is borrowed is named here; the page's other rows are three more
        // `UiSettingsMultiselectorBar` fields at +0x38, +0x50 and +0x58.

        /// <summary>UiSettingsMultiselectorBar — assist mode, the row that becomes AUTO.</summary>
        internal static int Panel_AssistMode = 0x80;        // assistMode

        /// <summary>
        /// Constrained2D — which object the hover tooltip is currently showing for, or null.
        ///
        /// <para>
        /// `_vF` writes it on the way in and the panel's own `Update` reads it to place the tooltip, so
        /// it is the game's own answer to "is the tooltip up, and for whom" — which is what makes it
        /// safe to put a different sentence in the tooltip without disturbing the other rows that share
        /// it.
        /// </para>
        /// </summary>
        internal static int Panel_TooltipTarget = 0xC0;     // _Zi

        // ---------------------------------------------------------------- UiSettingsMultiselectorBar
        //
        // One option row: a title, a row of option buttons, and a callback the panel binds.

        /// <summary>
        /// Constrained2D — the row's own layout component.
        ///
        /// <para>
        /// Read to answer one question: does the page's hover tooltip have this row up? `_vF` stores
        /// the object it is showing for at panel+0xC0, and comparing that against the row's own `self`
        /// is what keeps the mod's line out of the other rows' tooltips.
        /// </para>
        /// </summary>
        internal static int Bar_Self = 0x20;                // self

        /// <summary>UIButton[] — one per option. Read only, by the probe: it is how many options the
        /// row has.</summary>
        internal static int Bar_Buttons = 0x28;             // buttons

        /// <summary>
        /// Constrained2D — the row's "this setting is unavailable" visuals.
        ///
        /// <para>
        /// `_YG(0)` turns this one on and <see cref="Bar_ActiveElements"/> off, so the pair is the row's
        /// own way of showing that it is disabled. Read as one of the two signals for whether the row
        /// is in use — the other being the page's flags, which is what actually decides.
        /// </para>
        /// </summary>
        internal static int Bar_DisabledElements = 0x38;    // disabledElements

        /// <summary>Constrained2D — the row's normal visuals; see <see cref="Bar_DisabledElements"/>.</summary>
        internal static int Bar_ActiveElements = 0x40;      // activeElements

        /// <summary>FastText[] — the row's title text(s).</summary>
        internal static int Bar_TitleTexts = 0x58;          // titleTexts

        /// <summary>
        /// Action&lt;UiSettingsMultiselectorBar, int&gt; — an option was pressed; the int is the option
        /// index.
        ///
        /// <para>
        /// Read only, to report whether the row is still wired: the mod does not write this. The
        /// click closure (`UiSettingsMultiselectorBar._Jb._Ah`) plays a click sound and then invokes
        /// it, and the mod answers the press one level further along, in the page's own handler — see
        /// <see cref="AssistRow.HandlePress"/>. Never write it here: rebinding orphans the game's
        /// delegates (<see cref="Hooks.AttachBarValueChanged"/> has the reason).
        /// </para>
        /// </summary>
        internal static int Bar_OnValueChanged = 0x68;      // _kj

        /// <summary>int — the selected option index. `_xG(index)` writes it.</summary>
        internal static int Bar_Index = 0x70;               // _Kj

        /// <summary>_Kb, the option's own value. `_YG(value)` writes it.</summary>
        internal static int Bar_Value = 0x74;               // _lj

        // ---------------------------------------------------------------- the scroll list
        //
        // TRAP: `optionsScrollView` is a `UIScrollListView` (derives from `UIScrollInterface`) and
        // is not a `UIScrollView`. They agree for the first few members and then diverge, and are
        // keyed by different numbers:
        //
        //     +0x38  contentContainer                both
        //     +0x80  (UIScrollListView) ContentContainerBoundY     ← the scroll clamp bound
        //     +0x84  (UIScrollListView) ContentContainerBoundX
        //     +0x88  (UIScrollView)     OrderedElements            ← its own element list
        //     +0x88  (UIScrollListView) OnPositionUpdated          ← an Action, not a list
        //
        // Calling a `UIScrollView` method on it reads and writes the wrong fields. Nothing here uses
        // any of this — the mod does not touch the scroll range — but a row of its own would have had
        // to.

        // ---------------------------------------------------------------- the row's own methods

        /// <summary>
        /// `UiSettingsMultiselectorBar._xG(index)` — `void(self, index, methodInfo)`.
        /// Selects an option and repaints the buttons.
        /// </summary>
        internal const long Rva_Bar_SelectIndex = 0x6D2490;

        /// <summary>
        /// `UiSettingsMultiselectorBar._YG(value)` — `void(self, value, methodInfo)`.
        /// Sets the option's value and switches the row's active/disabled elements.
        /// </summary>
        internal const long Rva_Bar_SetValue = 0x6D2070;

        // ---------------------------------------------------------------- FastText

        /// <summary>
        /// `FastText.textParameters` — a 0x50-byte `TextParameters` whose first field is the
        /// `Strings.Key` the text is localized from.
        ///
        /// <para>
        /// Read and written as one opaque block rather than field by field: the struct also carries a
        /// layout selector and a runtime-built string, and copying the whole thing is what makes
        /// putting the original text back exact — including for a text that was not a plain key to
        /// begin with.
        /// </para>
        /// </summary>
        internal static int FastText_TextParameters = 0x38;   // textParameters

        /// <summary>
        /// sizeof(TextParameters) — the next FastText field starts at +0x88. A size, so it is measured
        /// from the two fields that bracket it (see <see cref="FieldResolver.Between"/>).
        /// </summary>
        internal static int TextParameters_Size = 0x50;

        /// <summary>
        /// `FastText.SetTextNonLocalized(string, UpdateType)` — `void(self, il2cppString, updateType)`.
        ///
        /// <para>
        /// The only text setter that takes a literal instead of a `Strings.Key`. The enum of
        /// localization keys is compiled into the game, so a mod cannot add "AUTO" to it — this is the
        /// way round that.
        /// </para>
        /// </summary>
        internal const long Rva_FastText_SetNonLocalized = 0x42E950;

        /// <summary>
        /// `FastText.SetText(in TextParameters, UpdateType)` — `void(self, TextParameters*, updateType)`.
        ///
        /// <para>
        /// The way back: it re-applies a saved `TextParameters`, which is how the original title is put
        /// back when the row becomes usable again. Without it the row keeps the mod's literal for the
        /// rest of the scene.
        /// </para>
        /// </summary>
        internal const long Rva_FastText_SetTextParameters = 0x42EBB0;

        // ---------------------------------------------------------------- the shared tooltip
        //
        // The sentence under a row is not the row's: `_vF` (0x6C0C30) builds a TextParameters from a
        // localization key and writes it into one shared tooltip object, which it takes from a
        // singleton. There is nothing on the row that points at it, so it is reached the way the game
        // reaches it.
        //
        //     klass     = *(GameAssembly + 0x318FFA8)      qword_18318FFA8, an Il2CppClass*
        //     statics   = *(klass + 184)
        //     singleton = *(statics + 0)
        //     tooltip   = *(singleton + 72)                 the component that draws the sentence
        //
        // The sentence was measured to be localization key 1403 on this build; the mod writes its
        // own line over whatever is there, so a different key still works.

        /// <summary>RVA of the class whose first static field holds the tooltip's owner.</summary>
        internal const long Rva_TooltipOwnerClass = 0x318FFA8;

        /// <summary>Offset of the class's statics block, as every IL2CPP class has it.</summary>
        internal const int Class_Statics = 184;

        /// <summary>Offset of the tooltip-drawing component on the singleton — `CoreScene.UiTextToolTip`.</summary>
        internal static int Singleton_Tooltip = 72;   // UiTextToolTip

        /// <summary>
        /// Asks the running game for the offset of every field above, and keeps the value written
        /// here for anything it cannot resolve. Called once, before any hook is installed.
        /// </summary>
        internal static void Resolve()
        {
            Panel_AssistMode = FieldResolver.Field("UiSettingGameplayPanel", "assistMode", Panel_AssistMode);
            Panel_TooltipTarget = FieldResolver.Field("UiSettingGameplayPanel", "_Zi", Panel_TooltipTarget);

            Bar_Self = FieldResolver.Field("UiSettingsMultiselectorBar", "self", Bar_Self);
            Bar_Buttons = FieldResolver.Field("UiSettingsMultiselectorBar", "buttons", Bar_Buttons);
            Bar_DisabledElements = FieldResolver.Field("UiSettingsMultiselectorBar", "disabledElements", Bar_DisabledElements);
            Bar_ActiveElements = FieldResolver.Field("UiSettingsMultiselectorBar", "activeElements", Bar_ActiveElements);
            Bar_TitleTexts = FieldResolver.Field("UiSettingsMultiselectorBar", "titleTexts", Bar_TitleTexts);
            Bar_OnValueChanged = FieldResolver.Field("UiSettingsMultiselectorBar", "_kj", Bar_OnValueChanged);
            Bar_Index = FieldResolver.Field("UiSettingsMultiselectorBar", "_Kj", Bar_Index);
            Bar_Value = FieldResolver.Field("UiSettingsMultiselectorBar", "_lj", Bar_Value);

            FastText_TextParameters = FieldResolver.Field("FastText", "textParameters", FastText_TextParameters);

            // The block's size is the distance to the next field, and both ends resolve by name —
            // so this is measured from the game, not assumed.
            TextParameters_Size = FieldResolver.Between(
                "TextParameters_Size",
                FastText_TextParameters,
                FieldResolver.Lookup("FastText", "fontSize"),
                TextParameters_Size);

            // The one field in this file that is not on one of the panel's own types: the tooltip
            // lives on the CoreScene singleton, which is reached by pointer rather than by type.
            Singleton_Tooltip = FieldResolver.Field("CoreScene", "UiTextToolTip", Singleton_Tooltip);
        }
    }
}
