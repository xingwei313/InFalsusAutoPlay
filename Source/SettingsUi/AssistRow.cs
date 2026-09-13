extern alias UnityEngineCore;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngineCore::UnityEngine;

// The generated interop namespaces keep the game's own with the generator's prefix. FastText is named
// only as an il2cpp type argument (see SetAllTexts) — the objects that come back are base
// `Component` proxies, so no alias for it is needed here.

namespace InFalsusAutoPlay
{
    /// <summary>
    /// The AUTO switch. It is made by taking over a row the page already has, not by adding one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The row is the card-crafting assist mode (`UiSettingGameplayPanel.assistMode`, at panel+0x80).
    /// The page puts that row out of use outside the title screen, and this only ever touches it while
    /// the page says so: it says so by putting flag 46 in the array it hands `_WF`, which is the
    /// page's own decision rather than a guess from how the row looks.
    /// </para>
    /// <para>
    /// A row is borrowed rather than built because a new object has to be correct in four separate
    /// systems at once — the layout records the geometry is drawn from, the content mask, the scroll
    /// view's range, and whatever recomputes a layout record — and each of them fails silently in its
    /// own way. A row the game made is already correct in all four.
    /// </para>
    /// <para>
    /// There is no separate "taken" flag: the row is borrowed exactly while <see cref="Current"/> is
    /// not null, and the two are set and cleared together. Nothing sets either one alone, so a claim
    /// cannot outlive the detour that serves it and a detour cannot outlive the claim.
    /// </para>
    /// <para>What is done to a borrowed row, and given back on release:</para>
    /// <list type="bullet">
    /// <item><description>
    /// Title. Written with `SetTextNonLocalized`, with the original `TextParameters` block copied out
    /// first and restored on release.
    /// </description></item>
    /// <item><description>
    /// Presses. The row keeps the game's own callback, and the mod intercepts the handler that
    /// callback reaches (`_eg`; see <see cref="HandlePress"/>). Rebinding the row instead is unsafe:
    /// it orphans the game's delegates, and copies kept on this side are invisible to the il2cpp
    /// collector.
    /// </description></item>
    /// <item><description>
    /// State. The row is kept usable (`_YG(row, 1)`) and the setting's value shown as the option
    /// index (`_xG`), re-asserted every frame because the page states its own opinion again after
    /// `_WF`.
    /// </description></item>
    /// </list>
    /// <para>
    /// The cost is real: while the row is borrowed, the assist setting cannot be changed from this
    /// screen. Its value is frozen, not corrupted.
    /// </para>
    /// </remarks>
    internal sealed unsafe class AssistRow
    {
        // ---------------------------------------------------------------- the claim

        /// <summary>The row currently borrowed, or null. See the class summary.</summary>
        internal static AssistRow Current { get; private set; }

        /// <summary>Which option index means "on". Option 0 is off, 1 is on, as in the game.</summary>
        private const int OptionOn = 1;

        /// <summary>
        /// The row's own state value meaning "usable". Not the setting: see <see cref="Apply"/>.
        /// </summary>
        private const int RowStateUsable = 1;

        /// <summary>
        /// The flag that means "the assist row is not in use on the page as it is now".
        ///
        /// <para>
        /// `_WF` takes an array of these and walks it, and one case per flag puts a row into its
        /// unavailable state — `_XG(row, 0, 323)`, which is `_YG(0)` plus a label key. The case for
        /// the assist row (the bar field at panel+0x80) is flag 46, so an array containing 46 is the
        /// page saying so in its own words.
        /// </para>
        /// </summary>
        private const int FlagAssistUnavailable = 46;

        // ---------------------------------------------------------------- what the row says
        //
        // Labels, not settings. These are not config keys: the wording is decided, so a file to change
        // it is surface that buys nothing. The game's own labels cannot be reused — they come from a
        // localization enum compiled into the game.

        /// <summary>The switch's own label, on the row.</summary>
        private const string RowLabel = "AUTO";

        /// <summary>The heading of the section it sits in, which the game calls "card crafting".</summary>
        private const string SectionLabel = "In Falsus Auto Play";

        /// <summary>
        /// The line under the switch. The row carries a sentence about the assist mode instead
        /// ("turning this off disables part of the game"), which says nothing about autoplay.
        /// </summary>
        private const string DescriptionLabel = "AutoPlay";

        // ---------------------------------------------------------------- native calls

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void BarIndexFn(IntPtr self, int index, IntPtr methodInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void BarValueFn(IntPtr self, int value, IntPtr methodInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void TextNonLocalizedFn(IntPtr self, IntPtr str, byte updateType);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetTextParametersFn(IntPtr self, IntPtr textParameters, byte updateType);

        private static BarIndexFn _select;
        private static BarValueFn _setValue;
        private static TextNonLocalizedFn _setText;
        private static SetTextParametersFn _setTextParameters;
        private static bool _resolved;

        // ---------------------------------------------------------------- this borrowing

        /// <summary>
        /// The page the row was borrowed from, and the row itself.
        ///
        /// <para>
        /// Both are fixed at take-over, and that is deliberate: the panel and the row come from one
        /// page, so asking "is the page still up" of the panel the row actually came from is what
        /// keeps a scene change from answering yes about a different page and letting the per-frame
        /// writes continue into a row that is gone. Refreshing the panel on every `_WF` while leaving
        /// the row where it was is the same mistake waiting for a page to be rebuilt.
        /// </para>
        /// </summary>
        private readonly IntPtr _panel;

        private readonly IntPtr _row;

        /// <summary>
        /// Every text this mod has overwritten, with the block that was there before.
        ///
        /// <para>
        /// The row is only borrowed: the page is handed it back the moment the page wants it, and then
        /// its title has to be the game's again. `SetTextNonLocalized` writes a literal and keeps
        /// nothing, so the original has to be copied out first.
        /// </para>
        /// </summary>
        private readonly List<SavedText> _saved = new List<SavedText>();

        /// <summary>
        /// The row's own description texts — the line under the switch.
        ///
        /// <para>
        /// Held by pointer and rewritten every frame rather than once: the game puts its sentence back
        /// from a localization key whenever the row is hovered or its state is restated, so a single
        /// write is undone by the next hover. Same shape as the row's own state, and the same answer.
        /// </para>
        /// </summary>
        private readonly List<IntPtr> _descriptions = new List<IntPtr>();

        /// <summary>
        /// The texts of the shared hover tooltip, once it has been reached.
        ///
        /// <para>
        /// Not saved and restored: the game rewrites that sentence from a localization key on every
        /// hover, so whatever is in there when the mod first looks is another row's line. Writing the
        /// mod's line while the tooltip is up for the borrowed row, and leaving it otherwise, is all
        /// that is needed.
        /// </para>
        /// </summary>
        private readonly List<IntPtr> _tooltipTexts = new List<IntPtr>();

        private sealed class SavedText
        {
            internal IntPtr Text;
            internal byte[] Parameters;
        }

        // Read by the status line only.
        internal long Syncs;
        internal long Presses;

        private AssistRow(IntPtr panel, IntPtr row)
        {
            _panel = panel;
            _row = row;
        }

        // ---------------------------------------------------------------- entry points

        /// <summary>
        /// From the `UiSettingGameplayPanel._WF` detour, after the original.
        ///
        /// <para>
        /// `_WF` is where the page's flags are applied, so it is where "does the page want this row" is
        /// answerable; it is also where the page pushes the real settings into its rows, so the mod's
        /// value has to be written after it rather than before.
        /// </para>
        /// <para>
        /// There is no `Config.SettingsRow` test here on purpose: that constant gates whether `_WF` is
        /// hooked at all, so with it off this is not merely guarded but unreachable.
        /// </para>
        /// </summary>
        internal static void SyncFromShow(IntPtr panel, IntPtr flags)
        {
            try
            {
                IntPtr row = Memory.Ptr(panel + SettingsOffsets.Panel_AssistMode);
                if (!Memory.LooksLikeObject(row))
                {
                    // A page is here and its row is not, so whatever was borrowed is not on this page.
                    // Give it up rather than leave the claim standing: the re-assert runs every frame
                    // off the borrowed row's own panel, and a claim kept across a page that could not be
                    // read is exactly the shape that ends in writes aimed at a torn-down object.
                    if (Current != null) Release("its assist row is not readable");
                    else NoteFailure("the assist row is not readable");
                    return;
                }

                // The flag decides. The row's looks are read beside it and logged, but they are not part
                // of the decision: acting on a symptom instead of the page's own statement is how a
                // take-over ends up happening on a screen that wanted the row.
                bool unavailable = FlagsContain(flags, FlagAssistUnavailable);
                bool byLooks = IsUnavailable(row, out string why);

                // Every use of this string is a log argument, so `[Conditional]` already deletes it from
                // a Release build — building it under the same guard as the text it feeds means Release
                // does not pay for the string, nor for reading the flag array again.
#if DEBUG
                string evidence = $"flag{FlagAssistUnavailable}={unavailable} {FlagsText(flags)} looks[{why}]";
#else
                string evidence = "";
#endif

                if (!unavailable)
                {
                    if (Current != null) Release(evidence);
                    else Diagnostics.Info($"AUTO row: the page wants the row ({evidence}); not touching it");
                    return;
                }

                if (byLooks != unavailable)
                    Diagnostics.Info($"AUTO row: the flag says unavailable but the row does not look it " +
                                     $"({evidence}); taking it over anyway");

                if (Current == null && TryTakeOver(panel, row) == null) return;

                // While borrowed the label is the mod's, every time the page is entered: the game
                // re-localizes text on its own schedule and would otherwise put its label back on a row
                // that is still the mod's switch.
                Current.SetTitle(RowLabel);

                ConfigFile.Load();
                Current.Apply(Config.Autoplay);
                Current.Syncs++;

                Diagnostics.Info($"AUTO row: synced - autoplay={(Config.Autoplay ? 1 : 0)} " +
                                 $"from {Config.Path} ({evidence})");
            }
            catch (Exception e)
            {
                NoteFailure($"sync failed: {Diagnostics.Describe(e)}");
            }
        }

        /// <summary>
        /// The per-frame re-assert, from the mod's own clock (<see cref="AutoPlayMod.OnLateUpdate"/>)
        /// rather than from a detour.
        ///
        /// <para>
        /// The page puts this row out of use outside the title screen — which is why there is a row to
        /// borrow — and that pass runs after `_WF`, so a take-over that wrote the state once would be
        /// undone a frame later and the switch would be dead. Cheap: two calls on one object, plus the
        /// texts the game keeps rewriting.
        /// </para>
        /// </summary>
        internal static void Reassert()
        {
            AssistRow row = Current;
            if (row == null || !row.PageIsUp()) return;

            row.Apply(Config.Autoplay);
            row.KeepDescription();
            row.KeepTooltip();
        }

        /// <summary>
        /// A press on the borrowed row's options, from the detour on the page's own handler.
        ///
        /// <para>
        /// Returns true when the press was the mod's to answer, which also stops the page's handling
        /// from running — for this row that is a confirmation dialog and then a change to the assist
        /// setting, neither of which should happen when the row is the mod's switch.
        /// </para>
        /// <para>
        /// The row is left as the game wired it: the game's callback still runs and still reaches the
        /// page, and this is where the page is told not to act on it. That is what makes the take-over
        /// safe to undo — there is no wiring to restore and nothing orphaned.
        /// </para>
        /// </summary>
        internal static bool HandlePress(IntPtr bar, int index)
        {
            AssistRow row = Current;
            if (row == null || bar != row._row) return false;

            try
            {
                bool on = index == OptionOn;
                row.Presses++;

                bool written = ConfigFile.Write(on);
                Diagnostics.Info($"AUTO row: pressed -> autoplay={(on ? 1 : 0)} " +
                                 $"(config {(written ? "written" : "NOT written")})");

                row.Apply(on);
                return true;
            }
            catch (Exception e)
            {
                NoteFailure($"press failed: {Diagnostics.Describe(e)}");
                return false;
            }
        }

        // ---------------------------------------------------------------- taking over, giving back

        /// <summary>
        /// Borrows the row, or returns null having touched nothing.
        ///
        /// <para>
        /// The claim is recorded at the moment there is a detour that needs it, and before any of the
        /// row's text is written: everything after that point writes to the row, and a throw in any of
        /// it would otherwise leave the detour on the page's handler with nothing owning it — a state
        /// <see cref="Release"/> is never reached from.
        /// </para>
        /// </summary>
        private static AssistRow TryTakeOver(IntPtr panel, IntPtr row)
        {
            if (!Resolve())
            {
                NoteFailure("the row calls could not be resolved; nothing was taken over");
                return null;
            }

            // The mod's answer to a press is delivered by a detour on the page's own handler, and that
            // detour exists only for as long as the row is borrowed — while the row is the game's,
            // nothing of the mod is anywhere near it.
            //
            // It goes on before the row is touched, and the take-over is abandoned if it will not:
            // a row labelled AUTO whose presses still reach the page is worse than a row left alone,
            // because pressing it raises the game's assist dialog and changes the assist setting under a
            // label that says otherwise.
            if (!Hooks.AttachBarValueChanged())
            {
                NoteFailure("the row option handler could not be hooked; the row is the game's");
                return null;
            }

            var taken = new AssistRow(panel, row);
            Current = taken;

            Diagnostics.Info($"AUTO row: took over the assist row @0x{row.ToInt64():X} as " +
                             $"'{RowLabel}' in section '{SectionLabel}' " +
                             "(the assist setting is frozen from here on)");

            taken.RetitleHeadings();
            taken.CollectDescriptions();
            return taken;
        }

        /// <summary>
        /// Gives the row back: the mod's claim on its presses and its per-frame re-assert first, then
        /// its texts. The row ends up the game's in every respect, with no detour of the mod's left on
        /// any function it reaches.
        ///
        /// <para>
        /// Called both when the page asks for the row again and when the page cannot be read at all, so
        /// <paramref name="evidence"/> is a reason rather than a verdict.
        /// </para>
        /// </summary>
        private static void Release(string evidence)
        {
            AssistRow row = Current;
            if (row == null) return;

            // Order matters: drop the claim (which is what stops the per-frame re-assert), stop
            // answering presses, take the detour off the page's own handler, and only then put the texts
            // back. After this the row is the game's in every respect — its text, its callbacks, and the
            // function they reach.
            Current = null;
            Hooks.DetachBarValueChanged();
            row.RestoreAll();
            row._descriptions.Clear();
            row._tooltipTexts.Clear();

            // The tooltip belongs to whichever scene built it, so the resolved pointer is dropped with
            // the borrowing.
            Tooltip.Forget();

            Diagnostics.Info($"AUTO row: handed the row back to the game ({evidence})");
        }

        // ---------------------------------------------------------------- the texts

        /// <summary>
        /// The row's own title: every text in `titleTexts`, which is the same text in each of the row's
        /// visual states.
        /// </summary>
        private void SetTitle(string text)
        {
            IntPtr array = Memory.Ptr(_row + SettingsOffsets.Bar_TitleTexts);
            if (!Memory.LooksLikeObject(array))
            {
                NoteFailure("the row's title texts are not readable");
                return;
            }

            int length = Memory.I32(array + Offsets.Runtime.ListSize);
            if (length < 0 || length > 16)
            {
                NoteFailure($"implausible title text count {length}");
                return;
            }

            IntPtr str = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(text);
            int written = 0;

            for (int i = 0; i < length; i++)
            {
                IntPtr fastText = Memory.Ptr(array + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size);
                if (!Memory.LooksLikeObject(fastText)) continue;

                if (SaveAndSet(fastText, str)) written++;
            }

            Diagnostics.Info($"AUTO row: row title set to '{text}' on {written}/{length} text(s)");
        }

        /// <summary>
        /// The heading of the section the rows sit in, and the row's own description text.
        ///
        /// <para>
        /// Both are retitled or rewritten by <see cref="SetAllTexts"/>; what is tricky is finding them,
        /// and neither is where they would be expected:
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        /// The heading is not necessarily a child of the row's container — a section can be laid out as
        /// a heading next to a list rather than inside it — so the search walks up two levels and looks
        /// at the direct children of each. Rows are never descended into, which is what keeps every
        /// other row's title safe.
        /// </description></item>
        /// <item><description>
        /// The description ("turning this off disables part of the game" — true of the assist mode,
        /// meaningless for a switch that is not the assist mode) hangs off the row itself, under a
        /// `...HoverTooltip` object. Anything the row carries whose name says Description is claimed.
        /// </description></item>
        /// </list>
        /// <para>
        /// Every object considered is logged with its name and the localization key of each text on it,
        /// so a heading that is somewhere else again is a line in the log rather than a guess.
        /// </para>
        /// </summary>
        private void RetitleHeadings()
        {
            try
            {
                Transform container = new Component(_row).transform.parent;
                if (container == null) return;

                Transform level = container;
                for (int up = 0; up < 2 && level != null; up++)
                {
                    int n = level.childCount;
                    int headings = 0;

                    for (int i = 0; i < n; i++)
                    {
                        Transform child = level.GetChild(i);
                        if (child == null) continue;

                        string name = child.name;
                        if (name == null) continue;
                        if (name.IndexOf("title", StringComparison.OrdinalIgnoreCase) < 0) continue;

                        headings += SetAllTexts(child.gameObject, SectionLabel, name);
                    }

                    Diagnostics.Info($"AUTO row: heading search at '{level.name}' ({n} children): " +
                                     $"{headings} text(s) retitled");

                    level = level.parent;
                }
            }
            catch (Exception e)
            {
                NoteFailure($"retitling the surroundings: {Diagnostics.Describe(e)}");
            }
        }

        /// <summary>
        /// Finds the row's own description texts — the line it shows while it is disabled — writes the
        /// mod's line into them, and records them so <see cref="KeepDescription"/> can keep saying it.
        ///
        /// <para>
        /// This is the row's subtree only, by name. The sentence that appears when the row is hovered is
        /// not here: it belongs to a tooltip shared by the whole page, and it is
        /// <see cref="KeepTooltip"/> that deals with it — see <see cref="Tooltip"/> for why that is a
        /// different mechanism entirely.
        /// </para>
        /// </summary>
        private void CollectDescriptions() => WalkForDescriptions(new Component(_row).transform);

        private void WalkForDescriptions(Transform t)
        {
            if (t == null) return;

            string name = t.name;
            if (name != null && name.IndexOf("Description", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                int found = SetAllTexts(t.gameObject, DescriptionLabel, name, _descriptions);
                Diagnostics.Info($"AUTO row: '{name}' will be kept saying '{DescriptionLabel}' " +
                                 $"({found} text(s))");
                return;
            }

            int n = t.childCount;
            for (int i = 0; i < n; i++) WalkForDescriptions(t.GetChild(i));
        }

        /// <summary>
        /// Keeps the hover sentence the mod's — but only while the tooltip is up for the borrowed row.
        ///
        /// <para>
        /// The sentence is written into one tooltip shared by every row, by the game, from a
        /// localization key, on each hover — so it cannot be replaced once and forgotten. What makes it
        /// safe to write into a shared object is the panel's own bookkeeping: `panel + 0xC0` is the
        /// object the tooltip is currently showing for, and `_vF` maintains it. When the answer is
        /// anything else, the tooltip is somebody else's and is left alone.
        /// </para>
        /// </summary>
        private void KeepTooltip()
        {
            try
            {
                if (Memory.Ptr(_panel + SettingsOffsets.Panel_TooltipTarget)
                    != Memory.Ptr(_row + SettingsOffsets.Bar_Self)) return;

                IntPtr tooltip = Tooltip.Resolve();
                if (tooltip == IntPtr.Zero) return;

                if (_tooltipTexts.Count == 0) CollectTooltipTexts(tooltip);
                if (_tooltipTexts.Count == 0) return;

                WriteAll(_tooltipTexts, DescriptionLabel);
            }
            catch (Exception e)
            {
                NoteFailure($"keeping the hover sentence: {Diagnostics.Describe(e)}");
            }
        }

        /// <summary>Finds the tooltip's texts once, and says what is in there.</summary>
        private void CollectTooltipTexts(IntPtr tooltip)
        {
            try
            {
                GameObject go = new Component(tooltip).gameObject;
                if (go == null) return;

                var texts = go.GetComponentsInChildren(
                    Il2CppInterop.Runtime.Il2CppType.Of<Il2CppFastText.FastText>(), true);

                if (texts == null) return;

                foreach (Component t in texts)
                {
                    if (t == null) continue;
                    _tooltipTexts.Add(t.Pointer);

#if DEBUG
                    int key = Memory.I32(t.Pointer + SettingsOffsets.FastText_TextParameters);
                    Diagnostics.Info($"AUTO row: hover tooltip text '{NameOf(t)}' key={key}");
#endif
                }
            }
            catch (Exception e)
            {
                NoteFailure($"reading the hover tooltip's texts: {Diagnostics.Describe(e)}");
            }
        }

        /// <summary>
        /// Writes the mod's line over the remembered description texts.
        ///
        /// <para>
        /// The game writes its own sentence into that line whenever the row is hovered or its state is
        /// restated, so the mod's text is written again here rather than once at take-over.
        /// </para>
        /// </summary>
        private void KeepDescription()
        {
            if (_descriptions.Count == 0) return;

            try
            {
                WriteAll(_descriptions, DescriptionLabel);
            }
            catch (Exception e)
            {
                NoteFailure($"keeping the description text: {Diagnostics.Describe(e)}");
            }
        }

        private void WriteAll(List<IntPtr> texts, string text)
        {
            IntPtr str = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(text);
            foreach (IntPtr fastText in texts)
                if (Memory.LooksLikeObject(fastText)) _setText(fastText, str, 0);
        }

        /// <summary>
        /// Writes <paramref name="text"/> over every text under an object.
        ///
        /// <para>
        /// `GetComponentsInChildren` rather than `GetComponents`: a heading is usually one object with
        /// the text and its shadow on separate children, and the count is logged either way, because
        /// "found the object but no text on it" and "found nothing" are different problems. The
        /// localization key each text is currently bound to is logged beside it, which is what
        /// identifies a text when its object name does not.
        /// </para>
        /// </summary>
        private int SetAllTexts(GameObject go, string text, string label, List<IntPtr> keep = null)
        {
            IntPtr str = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(text);
            int written = 0;
            int found = 0;

            try
            {
                // The interop's own signature for this call is `Il2CppReferenceArray<Component>` — it
                // does not hand back concrete proxies, so the elements are iterated as `Component` and
                // only their pointers are used. (Measured: casting an element to the concrete type
                // throws InvalidCastException.)
                var texts = go.GetComponentsInChildren(
                    Il2CppInterop.Runtime.Il2CppType.Of<Il2CppFastText.FastText>(), true);

                if (texts != null)
                {
                    foreach (Component t in texts)
                    {
                        if (t == null) continue;
                        found++;

                        if (SaveAndSet(t.Pointer, str)) written++;
                        if (keep != null && !keep.Contains(t.Pointer)) keep.Add(t.Pointer);

#if DEBUG
                        int key = Memory.I32(t.Pointer + SettingsOffsets.FastText_TextParameters);
                        Diagnostics.Info($"AUTO row:   '{NameOf(t)}' key={key} -> '{text}'");
#endif
                    }
                }
            }
            catch (Exception e)
            {
                NoteFailure($"GetComponentsInChildren<FastText> on '{label}': {Diagnostics.Describe(e)}");
            }

            if (found == 0) Diagnostics.Info($"AUTO row:   '{label}' has no FastText under it");

            return written;
        }

        // ---------------------------------------------------------------- text save and restore

        /// <summary>
        /// Remembers what a text said, then writes the literal over it. The remembering happens once per
        /// text: a second take-over in the same scene must not replace the record of the original with
        /// the mod's own literal.
        /// </summary>
        private bool SaveAndSet(IntPtr fastText, IntPtr str)
        {
            try
            {
                Remember(fastText);
                _setText(fastText, str, 0);
                return true;
            }
            catch (Exception e)
            {
                NoteFailure($"set text: {Diagnostics.Describe(e)}");
                return false;
            }
        }

        private void Remember(IntPtr fastText)
        {
            foreach (SavedText s in _saved) if (s.Text == fastText) return;

            var block = new byte[SettingsOffsets.TextParameters_Size];
            Marshal.Copy(fastText + SettingsOffsets.FastText_TextParameters, block, 0, block.Length);

            _saved.Add(new SavedText { Text = fastText, Parameters = block });
        }

        /// <summary>
        /// Hands every borrowed text back: the block that was there is written back and the game's own
        /// setter is asked to re-apply it.
        /// </summary>
        private void RestoreAll()
        {
            if (_saved.Count == 0) return;

            int restored = 0;

            foreach (SavedText s in _saved)
            {
                if (!Memory.LooksLikeObject(s.Text)) continue;

                try
                {
                    Marshal.Copy(s.Parameters, 0, s.Text + SettingsOffsets.FastText_TextParameters,
                                 s.Parameters.Length);

                    fixed (byte* p = s.Parameters)
                        _setTextParameters(s.Text, (IntPtr)p, 0);

                    restored++;
                }
                catch (Exception e)
                {
                    NoteFailure($"restore text: {Diagnostics.Describe(e)}");
                }
            }

            _saved.Clear();
            Diagnostics.Info($"AUTO row: handed back {restored} text(s)");
        }

        // ---------------------------------------------------------------- applying a value

        /// <summary>
        /// Shows the setting's value on the row — and keeps the row usable while doing it.
        ///
        /// <para>The two calls do different jobs, which is the whole of this method:</para>
        /// <list type="bullet">
        /// <item><description>
        /// `_YG(row, 1)` is the row's state, not the setting. 1 is what the game itself passes for a row
        /// it wants usable (`_WF` does exactly this for the assist row), while 0 turns on the row's
        /// disabled visuals and puts its buttons into their disabled state. Writing the setting's
        /// boolean here instead makes the switch dead whenever autoplay is off.
        /// </description></item>
        /// <item><description>
        /// `_xG(row, option)` selects which of the two options is lit: 0 off, 1 on.
        /// </description></item>
        /// </list>
        /// </summary>
        private void Apply(bool on)
        {
            try
            {
                _setValue(_row, RowStateUsable, IntPtr.Zero);
                _select(_row, on ? OptionOn : 0, IntPtr.Zero);
            }
            catch (Exception e)
            {
                NoteFailure($"apply failed: {Diagnostics.Describe(e)}");
            }
        }

        // ---------------------------------------------------------------- availability

        /// <summary>
        /// Whether the page's flag array says the assist row is out of use.
        ///
        /// <para>
        /// The array is an `int[]`, so it is read the way every other IL2CPP array here is: length at
        /// +0x18, elements at +0x20. A page passes a handful of flags, so a linear scan is the whole of
        /// it.
        /// </para>
        /// </summary>
        private static bool FlagsContain(IntPtr flags, int value)
        {
            if (!Memory.LooksLikeObject(flags)) return false;

            int length = Memory.I32(flags + Offsets.Runtime.ListSize);
            if (length <= 0 || length > 64) return false;

            IntPtr data = flags + Offsets.Runtime.ArrayDataOffset;
            for (int i = 0; i < length; i++)
                if (Memory.I32(data + i * 4) == value) return true;

            return false;
        }

#if DEBUG
        /// <summary>
        /// What is actually in the page's flag array, for the log — e.g. `flags=[16,46]`.
        ///
        /// <para>
        /// The mod acts on one flag (46, the assist row), but the array it arrives in is always one of
        /// three sets the game builds once and never changes: `_Db..cctor()` (0x6C24B0) makes
        /// `_Db._lH` = `[34]`, `_Db._LH` = `[16, 46]` and `_Db._mH` (six values, copied from a static
        /// blob), and no other code writes them. Only `_LH` holds 46, so which set arrived is the
        /// context — the page is saying this row is out of use, or it is not.
        /// </para>
        /// <para>
        /// Printing the contents rather than the single flag means a set this mod has never seen reads
        /// as itself in the log, instead of as "flag46=False, nothing happened". There is no
        /// title-screen check to point at anywhere in the game: the twelve entry points that reach `_WF`
        /// each pass one of those frozen sets, so the difference between the title screen and a song is
        /// decided by which entry point ran, at compile time, not by any runtime question.
        /// </para>
        /// </summary>
        private static string FlagsText(IntPtr flags)
        {
            if (!Memory.LooksLikeObject(flags)) return "flags=<unreadable>";

            int length = Memory.I32(flags + Offsets.Runtime.ListSize);
            if (length <= 0 || length > 64) return $"flags=<implausible length {length}>";

            IntPtr data = flags + Offsets.Runtime.ArrayDataOffset;
            var text = new System.Text.StringBuilder("flags=[");

            for (int i = 0; i < length; i++)
            {
                if (i > 0) text.Append(',');
                text.Append(Memory.I32(data + i * 4));
            }

            return text.Append(']').ToString();
        }
#endif

        /// <summary>
        /// How the row looks, for the log's second opinion on the flag: its object inactive, or its
        /// disabled visuals shown without its active ones.
        /// </summary>
        private static bool IsUnavailable(IntPtr row, out string why)
        {
            bool live = false;

            try
            {
                live = new Component(row).gameObject.activeInHierarchy;
            }
            catch (Exception e)
            {
                NoteFailure($"reading the row's active state: {Diagnostics.Describe(e)}");
            }

            bool disabledVisual = SubObjectActive(row, SettingsOffsets.Bar_DisabledElements);
            bool activeVisual = SubObjectActive(row, SettingsOffsets.Bar_ActiveElements);

            why = $"live={live} disabledVisual={disabledVisual} activeVisual={activeVisual}";

            return !live || (disabledVisual && !activeVisual);
        }

        private static bool SubObjectActive(IntPtr row, int offset)
        {
            try
            {
                IntPtr c2d = Memory.Ptr(row + offset);
                if (!Memory.LooksLikeObject(c2d)) return false;

                GameObject go = new Component(c2d).gameObject;
                return go != null && go.activeSelf;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Whether the page this row was borrowed from is still up.
        ///
        /// <para>
        /// The page's own `Update` runs only while the page is active, so on the mod's own clock this
        /// has to be asked. The answer matters: the page being gone is exactly when the pointers it lent
        /// stop meaning anything, so skipping then is also what keeps a scene change from leaving
        /// per-frame writes aimed at a torn-down object.
        /// </para>
        /// </summary>
        private bool PageIsUp()
        {
            if (!Memory.LooksLikeObject(_panel)) return false;

            try
            {
                GameObject go = new Component(_panel).gameObject;
                return go != null && go.activeInHierarchy;
            }
            catch
            {
                return false;
            }
        }

        // ---------------------------------------------------------------- plumbing

        /// <summary>
        /// Finds the four game functions the row is driven through, once.
        ///
        /// <para>
        /// Two routes, chosen per function. `_xG` and `_YG` are the only methods of their names on their
        /// type, so they go through <see cref="MethodResolver.ByName"/> and get the update-proof
        /// MethodInfo lookup with the RVA as its fallback. The two `FastText` setters need more: that
        /// type has three one-argument `SetText` overloads and two `SetTextNonLocalized` ones, so a name
        /// alone picks the wrong one silently. They go through
        /// <see cref="MethodResolver.BySignature"/>, which adds the first parameter's type — the one
        /// thing all five differ in.
        /// </para>
        /// <para>
        /// A failure is not retried. A build in which these cannot be found will not start finding them
        /// later, and a retry would repeat four warnings per attempt.
        /// </para>
        /// </summary>
        private static bool Resolve()
        {
            if (_resolved)
                return _select != null && _setValue != null &&
                       _setText != null && _setTextParameters != null;
            _resolved = true;

            IntPtr select = MethodResolver.ByName("UiSettingsMultiselectorBar", "_xG",
                                                   SettingsOffsets.Rva_Bar_SelectIndex);
            IntPtr setValue = MethodResolver.ByName("UiSettingsMultiselectorBar", "_YG",
                                                     SettingsOffsets.Rva_Bar_SetValue);

            // By name and first parameter, with the RVA as the fallback — see the summary above for
            // why a name on its own is not enough here.
            IntPtr setText = MethodResolver.BySignature(
                "FastText", "SetTextNonLocalized", "System.String",
                SettingsOffsets.Rva_FastText_SetNonLocalized,
                "FastText.SetTextNonLocalized(string)");

            IntPtr setParams = MethodResolver.BySignature(
                "FastText", "SetText", "Il2CppStr.TextParameters",
                SettingsOffsets.Rva_FastText_SetTextParameters,
                "FastText.SetText(in TextParameters)");

            if (select != IntPtr.Zero) _select = Marshal.GetDelegateForFunctionPointer<BarIndexFn>(select);
            if (setValue != IntPtr.Zero) _setValue = Marshal.GetDelegateForFunctionPointer<BarValueFn>(setValue);
            if (setText != IntPtr.Zero) _setText = Marshal.GetDelegateForFunctionPointer<TextNonLocalizedFn>(setText);
            if (setParams != IntPtr.Zero) _setTextParameters = Marshal.GetDelegateForFunctionPointer<SetTextParametersFn>(setParams);

            return _select != null && _setValue != null &&
                   _setText != null && _setTextParameters != null;
        }

        private static string NameOf(Component c)
        {
            try
            {
                GameObject go = c.gameObject;
                return go == null ? "?" : go.name;
            }
            catch { return "?"; }
        }

        /// <summary>
        /// Reports without switching autoplay off — the settings screen is not gameplay.
        ///
        /// <para>
        /// Conditional, so that in a Release build the calls and every message they are given disappear
        /// with them. The counter it keeps is diagnostics too, so nothing is lost.
        /// </para>
        /// </summary>
        [System.Diagnostics.Conditional("DEBUG")]
        private static void NoteFailure(string what)
        {
#if DEBUG
            _failures++;
            if (_failures <= 5) Diagnostics.Warn($"AUTO row: {what}");
#endif
        }

#if DEBUG
        private static int _failures;
#endif

        /// <summary>A press that could not be answered: the page's own handling is allowed through.</summary>
        [System.Diagnostics.Conditional("DEBUG")]
        internal static void NotePressFailure(Exception e)
            => NoteFailure($"press threw, the page will handle it: {Diagnostics.Describe(e)}");
    }
}
