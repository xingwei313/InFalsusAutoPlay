using System;
using System.Collections.Generic;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// The chart, read out of the running game rather than out of a file. One instance per song:
    /// constructing it is what "forget the last chart" means.
    ///
    /// `LogicalNotePlayer` holds a `_T` at LNP+0x40. `_Hb()` reaches the per-plane note arrays as
    /// `*(*(lnp+0x40) + 0x10)`, and `_T`'s field list matches that and three other accessors —
    /// +0x10 `_vC` (`Dictionary&lt;_CA,_hA&gt;`), +0x38 `_XC`, +0x60 `_ZC`, +0x70 `_ad`. Each `_hA`
    /// carries its plane's notes as an `_fA[]` at +0x10. That is path B, and it is what the game
    /// itself walks.
    ///
    /// Path A reads `ValueTuple&lt;_t, List&lt;_fA&gt;, List&lt;_eA&gt;&gt;` from LNP+0x20 (a six-instruction
    /// getter copies it out; `_t` is 12 bytes padded to 16, then two List references, so the note
    /// list is at +0x30). It is kept as a cross-check — the two agreeing note-for-note is what
    /// turns "the offsets still line up" into a fact rather than a hope.
    ///
    /// Nothing here filters on anything the reader cannot verify. Do not add such a filter: one
    /// on "implausible" lane ranges silently removes a whole plane from the chart while the log
    /// goes on reporting a plausible note count.
    /// </summary>
    internal sealed class Chart
    {
        /// <summary>Every note, ordered by the time it has to be pressed.</summary>
        internal ChartNote[] Notes = Array.Empty<ChartNote>();

        /// <summary>
        /// Indices into <see cref="Notes"/>, ordered by the time each note finishes.
        ///
        /// This exists because the two orders differ: a long bar starting early can end after
        /// several later taps, so walking Notes and deciding on EndMs stalls the whole readback
        /// behind the bar. Ordered by EndMs, a single advancing cursor is exactly monotonic.
        /// </summary>
        internal int[] VerdictOrder = Array.Empty<int>();

        internal int SkyCount { get; private set; }

        /// <summary>_T._XC — where a note's judgement group, and so its grade, can be read.</summary>
        internal IntPtr GroupDict;

        /// <summary>Bumped every time a chart is published, so consumers can resync.</summary>
        internal int Revision { get; private set; }

        internal bool Loaded { get; private set; }

        /// <summary>Frames spent trying to read a chart that has not appeared.</summary>
        private const int MaxAttempts = 600;

        /// <summary>How many times a loaded chart may be looked at again. See <see cref="ShouldReRead"/>.</summary>
        private const int MaxReReads = 6;

        private const double ReloadGapMs = 3000.0;

        private int _reads;
        private int _attempts;
        private double _lastReadMs = double.NegativeInfinity;
        private int _publishedCount = -1;
        private bool _groupDictWarned;

#if DEBUG
        /// <summary>
        /// What the last read did, for the status line. Debug-only, and so are its writes: in a
        /// Release build nothing reads it, and a string nothing can read is weight for no reason —
        /// the same accounting <see cref="Quiet"/> is under.
        /// </summary>
        internal string Status { get; private set; } = "not loaded";
#endif

        /// <summary>
        /// Read (or re-read) the chart. Cheap once loaded: one bool test per frame.
        ///
        /// The re-read case is real — the sky plane's array can be absent on the frame the scene
        /// loads, and a chart cached at that moment would never grow it back — but it is bounded,
        /// because a song is allowed to have no sky notes at all.
        /// </summary>
        internal bool TryLoad(IntPtr lnp, double nowMs)
        {
            if (!Memory.LooksLikeObject(lnp)) return false;
            if (Loaded && !ShouldReRead(nowMs)) return true;

            if (!Loaded && _attempts >= MaxAttempts)
            {
                if (_attempts == MaxAttempts)
                    Diagnostics.Warn($"chart could not be read after {MaxAttempts} frames; " +
                                     "autoplay will do nothing");
                _attempts++;
                return false;
            }
            _attempts++;

            try
            {
                IntPtr chartObj = Memory.Ptr(lnp + Offsets.NotePlayer.ChartObject);
                IntPtr groupDict = Memory.LooksLikeObject(chartObj)
                    ? Memory.Ptr(chartObj + Offsets.ChartObject.GroupDict)
                    : IntPtr.Zero;

                // Keep a dictionary that worked rather than overwriting it with nothing. `_T` can
                // be unreadable for a frame mid-load while the note lists are already there, and
                // zeroing this would make every grade for the rest of the song read back as
                // unreadable — with the re-read budget already spent, so it never recovers.
                if (groupDict != IntPtr.Zero)
                {
                    GroupDict = groupDict;
                }
                else if (GroupDict == IntPtr.Zero && !_groupDictWarned)
                {
                    _groupDictWarned = true;
                    Diagnostics.Warn("_T._XC is unreadable; no grades can be read back this song");
                }

                bool okB = TryReadViaPlanes(lnp, out ChartNote[] viaPlanes);
                bool okA = TryReadViaList(lnp, out ChartNote[] viaList);
                _reads++;
                _lastReadMs = nowMs;

                // Both paths are read every time and compared; a disagreement is the only signal
                // that an offset has drifted, so it is worth the second pass.
                if (okA && okB) Compare(viaList, viaPlanes);
                else if (!okA && !okB) { Quiet("chart read found nothing"); return false; }

                // A one-sided failure is the case the re-read mechanism exists for — the sky plane
                // can be missing on the frame the scene loads — so it has to be visible rather than
                // silently accepted.
                else if (!okB) Diagnostics.Warn("chart path B (_T._vC -> _hA._ye) read nothing; using path A");
                else Diagnostics.Warn("chart path A (LNP+0x30 List) read nothing; using path B");

                Publish(lnp, okB ? viaPlanes : viaList, okB);
                return true;
            }
            catch (Exception e)
            {
#if DEBUG
                Status = "read threw";
#endif
                Diagnostics.Warn($"chart read failed: {Diagnostics.Describe(e)}");
                return false;
            }
        }

        private bool ShouldReRead(double nowMs)
        {
            if (_reads >= MaxReReads) return false;
            if (nowMs - _lastReadMs < ReloadGapMs) return false;

            // Only the missing-sky-plane case is worth a second look. A song with no sky notes
            // settles after MaxReReads and stops asking.
            return SkyCount == 0;
        }

        // ---------------------------------------------------------------- path B: what the game walks

        private bool TryReadViaPlanes(IntPtr lnp, out ChartNote[] notes)
        {
            notes = null;

            IntPtr chart = Memory.Ptr(lnp + Offsets.NotePlayer.ChartObject);
            if (!Memory.LooksLikeObject(chart)) return false;

            IntPtr dict = Memory.Ptr(chart + Offsets.ChartObject.PlaneArrays);
            if (!Memory.LooksLikeObject(dict)) return false;

            IntPtr entries = Memory.Ptr(dict + Offsets.Runtime.DictEntries);
            if (!Memory.LooksLikeObject(entries)) return false;

            int capacity = Memory.I32(entries + Offsets.Runtime.ListSize);
            if (capacity <= 0 || capacity > 256) return false;

            var all = new List<ChartNote>();
            IntPtr data = entries + Offsets.Runtime.ArrayDataOffset;

            for (int i = 0; i < capacity; i++)
            {
                int slot = i * Offsets.Runtime.DictEntrySize;

                // .NET stores hashCode & 0x7FFFFFFF for a live entry and -1 for a removed one, so a
                // non-negative hashCode is what distinguishes an occupied slot.
                if (Memory.I32(data + slot) < 0) continue;

                IntPtr holder = Memory.Ptr(data + slot + Offsets.Runtime.DictEntryValue);
                if (!Memory.LooksLikeObject(holder)) continue;

                IntPtr arr = Memory.Ptr(holder + Offsets.PlaneHolder.NoteArray);
                if (!Memory.LooksLikeObject(arr)) continue;

                int size = Memory.I32(arr + Offsets.Runtime.ListSize);
                if (size <= 0 || size > 200000) continue;

                // An `_fA` array is in hand, so its element size can be measured rather than assumed.
                // Everything below indexes it by that size.
                Offsets.Note.Size = FieldResolver.ElementSize(arr, "Note.Size", Offsets.Note.Size);

                if (Collect(arr, size, out ChartNote[] part)) all.AddRange(part);
            }

            if (all.Count == 0) return false;

            notes = all.ToArray();
            SortByTime(notes);
            return true;
        }

        // ---------------------------------------------------------------- path A: the cross-check

        private bool TryReadViaList(IntPtr lnp, out ChartNote[] notes)
        {
            notes = null;

            IntPtr list = Memory.Ptr(lnp + Offsets.NotePlayer.NoteList);
            if (!Memory.LooksLikeObject(list)) return false;

            IntPtr items = Memory.Ptr(list + Offsets.Runtime.ListItems);
            int size = Memory.I32(list + Offsets.Runtime.ListSize);
            if (!Memory.LooksLikeObject(items) || size <= 0 || size > 200000) return false;
            if (!Collect(items, size, out notes)) return false;

            SortByTime(notes);
            return true;
        }

        /// <summary>
        /// Reads the notes out of an `_fA[]`. Deliberately permissive: the only things rejected are
        /// values that cannot be arithmetic — a plane index outside the enum, a nonsense timestamp,
        /// an end before the start. Lane indices and note types are taken as they come; the caller
        /// clamps lanes when it presses, and the type histogram is what identifies which `_HA` value
        /// means what. Filtering on a guess here is how a whole plane goes missing.
        /// </summary>
        private static bool Collect(IntPtr array, int size, out ChartNote[] notes)
        {
            var list = new List<ChartNote>(size);
            int badSide = 0, badTime = 0;

            for (int i = 0; i < size; i++)
            {
                IntPtr p = array + Offsets.Runtime.ArrayDataOffset + i * Offsets.Note.Size;

                int side = Memory.I32(p + Offsets.Note.Side);
                if (side < Offsets.Plane.None || side > Offsets.Plane.Sky) { badSide++; continue; }

                int start = Memory.I32(p + Offsets.Note.StartMs);
                int end = Memory.I32(p + Offsets.Note.EndMs);
                if (start <= 0 || end < start || start > 6 * 60 * 60 * 1000) { badTime++; continue; }

                list.Add(new ChartNote(
                    start, end,
                    Memory.I32(p + Offsets.Note.LaneFirst),
                    Memory.I32(p + Offsets.Note.LaneLast),
                    Memory.I32(p + Offsets.Note.Type),
                    side,
                    Memory.F32(p + Offsets.Note.StartX),
                    Memory.F32(p + Offsets.Note.EndX),
                    Memory.F32(p + Offsets.Note.StartWidth),
                    Memory.F32(p + Offsets.Note.EndWidth),
                    Memory.I32(p + Offsets.Note.Flags),
                    Memory.I64(p + Offsets.Note.Id)));
            }

            // Every drop is counted. A silent filter is how a plane disappears.
            if (badSide > 0 || badTime > 0)
                Diagnostics.Info($"  (dropped {badSide} bad side, {badTime} bad time, of {size})");

            if (list.Count == 0) { notes = null; return false; }

            notes = list.ToArray();
            return true;
        }

        /// <summary>
        /// Both paths are ordered by time before anything looks at them, so the cross-check compares
        /// like with like: path A walks a List and path B walks dictionary slots, and those two
        /// orders have nothing in common.
        ///
        /// The note id is the tiebreaker because Array.Sort is not stable and chords are common —
        /// without it, two notes sharing a timestamp could come out in either order on either path,
        /// and the cross-check would report a drift that is not there.
        /// </summary>
        private static void SortByTime(ChartNote[] notes) =>
            Array.Sort(notes, (a, b) =>
            {
                int c = a.StartMs.CompareTo(b.StartMs);
                return c != 0 ? c : a.NoteId.CompareTo(b.NoteId);
            });

        /// <summary>
        /// Path A and path B agreeing note-for-note is what says the offsets are right. A mismatch
        /// is reported as a count rather than as a diff, because the useful question is "have the
        /// offsets drifted at all".
        /// </summary>
        private static void Compare(ChartNote[] a, ChartNote[] b)
        {
            if (a.Length != b.Length)
            {
                Diagnostics.Warn($"chart paths disagree: A={a.Length} notes, B={b.Length} notes");
                return;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].NoteId == b[i].NoteId && a[i].StartMs == b[i].StartMs && a[i].Side == b[i].Side)
                    continue;

                Diagnostics.Warn($"chart paths disagree at [{i}]: " +
                                 $"A id={a[i].NoteId} t={a[i].StartMs} side={a[i].Side} | " +
                                 $"B id={b[i].NoteId} t={b[i].StartMs} side={b[i].Side}");
                return;
            }
        }

        // ---------------------------------------------------------------- publishing

        /// <summary>
        /// Publishes a read chart. <paramref name="viaPlanes"/> says which of the two readers
        /// produced it; it is turned into a label only inside the Debug guard, so the labelling text
        /// is not carried by a Release build.
        /// </summary>
        private void Publish(IntPtr lnp, ChartNote[] notes, bool viaPlanes)
        {
            var order = new int[notes.Length];
            int sky = 0;

            for (int i = 0; i < notes.Length; i++)
            {
                order[i] = i;
                if (notes[i].IsSky) sky++;
            }

            Array.Sort(order, (a, b) =>
            {
                int c = notes[a].EndMs.CompareTo(notes[b].EndMs);
                return c != 0 ? c : notes[a].StartMs.CompareTo(notes[b].StartMs);
            });

            Notes = notes;
            VerdictOrder = order;
            SkyCount = sky;
            Loaded = true;
            Revision++;

#if DEBUG
            string how = viaPlanes ? "B: _T._vC -> _hA._ye" : "A: LNP+0x30 List";
            Status = $"{notes.Length} notes, {sky} sky, " +
                     $"{notes[0].StartMs / 1000.0:F1}s..{notes[notes.Length - 1].StartMs / 1000.0:F1}s via {how}";
            Diagnostics.Info($"chart loaded: {Status}");
#endif

            // The per-note detail is worth printing when the picture changes and is noise when it
            // does not, which is what the re-read case would otherwise produce.
            if (notes.Length == _publishedCount) return;
            _publishedCount = notes.Length;

#if DEBUG
            ChartProbe.DumpPlanes(lnp, viaPlanes);
            Describe(notes);
#endif
        }

        /// <summary>
        /// Reports a state that is worth printing once rather than every frame, Debug-only like
        /// everything else here that exists to be read rather than to play.
        ///
        /// `[Conditional]` as well as the guard inside, and both are needed: the attribute is what
        /// keeps the message out of a Release build — it removes the call, and with it the literal
        /// its argument is — while the guard is what keeps the body compiling, since `[Conditional]`
        /// removes calls and not the method itself.
        /// </summary>
        [System.Diagnostics.Conditional("DEBUG")]
        private void Quiet(string why)
        {
#if DEBUG
            if (Status == why) return;
            Status = why;
            Diagnostics.Info(why);
#endif
        }

        // ---------------------------------------------------------------- reading a grade back

        /// <summary>
        /// The game's own verdict for one note, read out of the judgement-group dictionary at
        /// `_T._XC`: `_cH._wdb` (+0x20, the grade) and `_cH._Wdb` (+0x21, whether it was judged).
        ///
        /// A linear scan of the entries array rather than a hashed lookup: it runs once per note,
        /// and matching the key directly avoids depending on the runtime's bucket layout.
        /// </summary>
        internal bool TryReadGroup(long noteId, out byte grade, out byte judged)
        {
            grade = 0;
            judged = 0;

            IntPtr dict = GroupDict;
            if (!Memory.LooksLikeObject(dict)) return false;

            IntPtr entries = Memory.Ptr(dict + Offsets.Runtime.DictEntries);
            if (!Memory.LooksLikeObject(entries)) return false;

            int capacity = Memory.I32(entries + Offsets.Runtime.ListSize);
            if (capacity <= 0 || capacity > 16384) return false;

            IntPtr data = entries + Offsets.Runtime.ArrayDataOffset;
            for (int i = 0; i < capacity; i++)
            {
                IntPtr e = data + i * Offsets.Runtime.GroupEntrySize;
                if (Memory.I32(e) < 0) continue;                              // free slot
                if (Memory.I64(e + Offsets.Runtime.GroupEntryKey) != noteId) continue;

                IntPtr mem = e + Offsets.Runtime.GroupEntryValue;
                IntPtr arr = Memory.Ptr(mem + Offsets.Runtime.MemoryObject);
                if (!Memory.LooksLikeObject(arr)) return false;

                // A `_cH` array is in hand, so its element size can be measured rather than assumed.
                Offsets.Judgement.Size = FieldResolver.ElementSize(arr, "Judgement.Size",
                                                                   Offsets.Judgement.Size);

                // The index is bounds-checked against the array's own length before it is used as a
                // multiplier. Without this, a slot that passed the key match but holds a stale or
                // unrelated value would compute an address anywhere in the address space, and the
                // read would be an access violation that no try/catch in this mod can catch.
                int length = Memory.I32(arr + Offsets.Runtime.ListSize);
                int index = Memory.I32(mem + Offsets.Runtime.MemoryIndex);
                if (length <= 0 || length > 65536 || index < 0 || index >= length) return false;

                IntPtr group = arr + Offsets.Runtime.ArrayDataOffset + index * Offsets.Judgement.Size;

                grade = Memory.U8(group + Offsets.Judgement.Grade);
                judged = Memory.U8(group + Offsets.Judgement.Judged);
                return true;
            }
            return false;
        }

        /// <summary>Lane/type histogram — which _HA value means what, and how many of each.</summary>
        private static void Describe(ChartNote[] notes)
        {
            var sides = new int[8];
            var types = new int[8];
            foreach (ChartNote n in notes)
            {
                if (n.Side >= 0 && n.Side < sides.Length) sides[n.Side]++;
                if (n.Type >= 0 && n.Type < types.Length) types[n.Type]++;
            }

            var sb = new System.Text.StringBuilder($"chart: {notes.Length} notes | sides");
            for (int i = 0; i < sides.Length; i++) if (sides[i] > 0) sb.Append($" {i}={sides[i]}");
            sb.Append(" | types");
            for (int i = 0; i < types.Length; i++) if (types[i] > 0) sb.Append($" {i}={types[i]}");
            sb.Append($" | {notes[0].StartMs / 1000.0:F1}s..{notes[notes.Length - 1].StartMs / 1000.0:F1}s");
            Diagnostics.Info(sb.ToString());

            for (int i = 0; i < notes.Length && i < 5; i++)
            {
                ChartNote n = notes[i];
                Diagnostics.Info($"  note[{i}] t={n.StartMs} end={n.EndMs} side={n.Side} type={n.Type} " +
                                 $"lanes={n.LaneFirst}..{n.LaneLast} x={n.StartX:F3}->{n.EndX:F3}");
            }
        }
    }
}
