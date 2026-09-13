extern alias UnityEngineCore;

using System;
using System.Text;
using UnityEngineCore::UnityEngine;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// What the AUTO switch says about itself as the page is entered — debug builds only.
    ///
    /// The take-over is conditional and it writes into a row the game owns, so almost every question
    /// about it is "what is actually there". These are the answers: the row's availability readings, the
    /// container's children, and every text found with the localization key it was bound to before being
    /// overwritten.
    ///
    /// <para>
    /// The reason this is a folder and not a flag: the file is not compiled into a Release build at all,
    /// because the project removes `Source\Probe` for that configuration. So call sites are written
    /// inside `#if DEBUG`, and a Release assembly carries none of this — not the calls, not the strings,
    /// not the reflection-heavy helpers.
    /// </para>
    ///
    /// What a Release build keeps is the events — the row was taken over, a press was answered, it was
    /// handed back — because those are a handful of lines that say what happened rather than what
    /// everything looked like.
    /// </summary>
    internal static class SettingsProbe
    {
        /// <summary>
        /// The row's state as the page is constructed, from the `Awake` detour — before anything has
        /// decided whether it will be used, so this is the only reading of the row in its freshly built
        /// state.
        /// </summary>
        internal static void RowState(IntPtr panel, string why)
        {
            IntPtr row = Memory.Ptr(panel + SettingsOffsets.Panel_AssistMode);
            if (!Memory.LooksLikeObject(row))
            {
                Diagnostics.Info($"AUTO row: probe ({why}) - the assist row at " +
                                 $"+0x{SettingsOffsets.Panel_AssistMode:X} is not readable");
                return;
            }

            var sb = new StringBuilder($"AUTO row: probe ({why}) - assist row @0x{row.ToInt64():X}");
            sb.Append($" name='{Name(row)}'");
            sb.Append($" live={Live(row)}");
            sb.Append($" disabledVisual={SubActive(row, SettingsOffsets.Bar_DisabledElements)}");
            sb.Append($" activeVisual={SubActive(row, SettingsOffsets.Bar_ActiveElements)}");
            sb.Append($" options={Options(row)}");
            sb.Append($" index={Memory.I32(row + SettingsOffsets.Bar_Index)}");
            sb.Append($" value={Memory.I32(row + SettingsOffsets.Bar_Value)}");
            sb.Append($" cb={(Memory.Ptr(row + SettingsOffsets.Bar_OnValueChanged) != IntPtr.Zero ? "bound" : "null")}");

            Diagnostics.Info(sb.ToString());
            DumpSiblings(row);
        }

        /// <summary>
        /// The container the row sits in and the names of its children — the section heading is found
        /// among them by name, so seeing the names is how a missing heading is diagnosed.
        /// </summary>
        private static void DumpSiblings(IntPtr row)
        {
            try
            {
                Transform parent = new Component(row).transform.parent;
                if (parent == null) return;

                var sb = new StringBuilder($"AUTO row: probe - container '{parent.name}' holds");
                int n = Math.Min(parent.childCount, 24);
                for (int i = 0; i < n; i++)
                {
                    Transform c = parent.GetChild(i);
                    sb.Append($" | {i}:'{(c == null ? "?" : c.name)}'");
                }

                Diagnostics.Info(sb.ToString());
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"AUTO row: probe could not walk the container " +
                                 $"({Diagnostics.Describe(e)})");
            }
        }

        private static string Name(IntPtr component)
        {
            try
            {
                GameObject go = new Component(component).gameObject;
                return go == null ? "?" : go.name;
            }
            catch { return "?"; }
        }

        private static string Live(IntPtr component)
        {
            try
            {
                GameObject go = new Component(component).gameObject;
                return go == null ? "?" : go.activeInHierarchy.ToString();
            }
            catch { return "?"; }
        }

        private static string SubActive(IntPtr row, int offset)
        {
            try
            {
                IntPtr c2d = Memory.Ptr(row + offset);
                if (!Memory.LooksLikeObject(c2d)) return "?";

                GameObject go = new Component(c2d).gameObject;
                return go == null ? "?" : go.activeSelf.ToString();
            }
            catch { return "?"; }
        }

        private static int Options(IntPtr row)
        {
            IntPtr array = Memory.Ptr(row + SettingsOffsets.Bar_Buttons);
            if (!Memory.LooksLikeObject(array)) return -1;
            return Memory.I32(array + Offsets.Runtime.ListSize);
        }
    }
}
