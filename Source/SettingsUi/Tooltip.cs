extern alias UnityEngineCore;

using System;
using UnityEngineCore::UnityEngine;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// The sentence that appears under a row when it is hovered does not belong to the row.
    ///
    /// <para>
    /// `UiSettingGameplayPanel._vF` (0x6C0C30) is what puts it there. Every row's hover callback calls
    /// it with the row's own `Constrained2D` and a `Strings.Key`, and it:
    /// </para>
    ///
    ///     tooltip = *(singleton + 72)                  one shared tooltip object
    ///     text    = build TextParameters from the key  the game's sentence, by localization key
    ///     set the tooltip's text, then show it
    ///     *(panel + 0xC0) = the row it is showing for
    ///
    /// <para>
    /// So the words are written into that shared object, and nothing on the row points at it: neither
    /// the row's own subtree nor the `tooltipHoverText` reference it carries is where the sentence
    /// lives.
    /// </para>
    /// <para>
    /// This class reaches the same object the game does — the class pointer, its statics block, the
    /// singleton, the component — so the mod can put its own line there. That is only safe because the
    /// tooltip is shared: `panel + 0xC0` says which row it is currently up for, and
    /// <see cref="AssistRow"/> writes into it only while the answer is the row it borrowed.
    /// </para>
    /// </summary>
    internal static class Tooltip
    {
        /// <summary>
        /// The class the tooltip lives on. Not a panel type: it is the `CoreScene` singleton, which
        /// every row's hover reaches through a static, so it is named rather than derived.
        /// </summary>
        private const string TooltipOwnerClass = "CoreScene";

        private static IntPtr _component;
        private static bool _looked;

        /// <summary>The component that draws the hover sentence, or Zero if it cannot be reached.</summary>
        internal static IntPtr Resolve()
        {
            if (_looked) return _component;
            _looked = true;

            try
            {
                // By name, so a game update that moves the class slot does not matter. The address of
                // the slot is only the fallback, and it is a class pointer rather than a method, so
                // nothing else can find it.
                IntPtr klass = FieldResolver.ClassPointer(TooltipOwnerClass);
                if (klass == IntPtr.Zero)
                {
                    klass = Memory.Ptr(GameAssembly.FromRva(SettingsOffsets.Rva_TooltipOwnerClass));

                    if (Memory.LooksLikeObject(klass))
                        Diagnostics.Warn($"{TooltipOwnerClass} was not found by name; the tooltip is " +
                                         "being reached through the address of its class slot");
                }

                if (!Memory.LooksLikeObject(klass))
                {
                    Diagnostics.Warn("the tooltip owner class is not initialised; the hover sentence " +
                                     "cannot be replaced");
                    return IntPtr.Zero;
                }

                IntPtr statics = Memory.Ptr(klass + SettingsOffsets.Class_Statics);
                if (!Memory.LooksLikeObject(statics)) return IntPtr.Zero;

                IntPtr singleton = Memory.Ptr(statics);
                if (!Memory.LooksLikeObject(singleton)) return IntPtr.Zero;

                IntPtr tooltip = Memory.Ptr(singleton + SettingsOffsets.Singleton_Tooltip);
                if (!Memory.LooksLikeObject(tooltip)) return IntPtr.Zero;

                _component = tooltip;
#if DEBUG
                Diagnostics.Info($"AUTO row: hover tooltip is 0x{tooltip.ToInt64():X} " +
                                 $"('{new Component(tooltip).gameObject.name}')");
#endif
                return _component;
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"could not reach the hover tooltip: {Diagnostics.Describe(e)}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Forgets the resolved object.
        ///
        /// <para>
        /// It belongs to whichever scene built it, and it is dropped with the borrowing so the next one
        /// resolves it again rather than writing into a scene that has been torn down.
        /// </para>
        /// </summary>
        internal static void Forget()
        {
            _component = IntPtr.Zero;
            _looked = false;
        }
    }
}
