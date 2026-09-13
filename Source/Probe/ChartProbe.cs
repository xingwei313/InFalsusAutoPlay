using System;
using System.Text;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// What the chart read says about itself — debug builds only.
    ///
    /// A chart read that is silently wrong is the failure mode this whole area has: the raw plane dump is
    /// the only way to tell "the plane is not there" from "the reader cannot see it". Neither is needed
    /// to play, so `Source\Probe` is not compiled into a Release build.
    /// </summary>
    internal static class ChartProbe
    {
        /// <summary>
        /// Dumps the plane dictionary slot by slot, before any filtering. The raw key, the array pointer,
        /// its length and the live count, for every occupied slot.
        /// </summary>
        internal static void DumpPlanes(IntPtr lnp, bool viaPlanes)
        {
            IntPtr chart = Memory.Ptr(lnp + Offsets.NotePlayer.ChartObject);
            if (!Memory.LooksLikeObject(chart)) { Diagnostics.Warn("_T (lnp+0x40) unreadable"); return; }

            IntPtr dict = Memory.Ptr(chart + Offsets.ChartObject.PlaneArrays);
            if (!Memory.LooksLikeObject(dict)) { Diagnostics.Warn("_T._vC unreadable"); return; }

            int count = Memory.I32(dict + Offsets.Runtime.DictCount);
            IntPtr entries = Memory.Ptr(dict + Offsets.Runtime.DictEntries);
            if (!Memory.LooksLikeObject(entries)) { Diagnostics.Warn("_T._vC entries unreadable"); return; }

            int capacity = Memory.I32(entries + Offsets.Runtime.ListSize);
            var sb = new StringBuilder($"planes: count={count} capacity={capacity} " +
                                       $"via={(viaPlanes ? "B" : "A")}");

            IntPtr data = entries + Offsets.Runtime.ArrayDataOffset;
            for (int i = 0; i < capacity && i < 32; i++)
            {
                int slot = i * Offsets.Runtime.DictEntrySize;
                int hash = Memory.I32(data + slot);
                int key = Memory.I32(data + slot + Offsets.Runtime.DictEntryKey);
                IntPtr holder = Memory.Ptr(data + slot + Offsets.Runtime.DictEntryValue);

                if (hash < 0) { sb.Append($" | [{i}] free"); continue; }
                if (!Memory.LooksLikeObject(holder)) { sb.Append($" | [{i}] key={key} holder=bad"); continue; }

                IntPtr arr = Memory.Ptr(holder + Offsets.PlaneHolder.NoteArray);
                int len = Memory.LooksLikeObject(arr) ? Memory.I32(arr + Offsets.Runtime.ListSize) : -1;
                int live = Memory.I32(holder + Offsets.PlaneHolder.Count);

                sb.Append($" | [{i}] key={key} notes={len} live={live}");

                // First entry of each plane, raw, so a filtering mistake is visible.
                if (len > 0)
                {
                    IntPtr p = arr + Offsets.Runtime.ArrayDataOffset;
                    sb.Append($" first(side={Memory.I32(p + Offsets.Note.Side)}" +
                              $" type={Memory.I32(p + Offsets.Note.Type)}" +
                              $" t={Memory.I32(p + Offsets.Note.StartMs)}" +
                              $" lanes={Memory.I32(p + Offsets.Note.LaneFirst)}..{Memory.I32(p + Offsets.Note.LaneLast)})");
                }
            }

            Diagnostics.Info(sb.ToString());
        }
    }
}
