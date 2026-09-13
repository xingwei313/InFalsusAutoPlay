namespace InFalsusAutoPlay
{
    /// <summary>
    /// Every IL2CPP offset the mod depends on, one nested group per game type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are not guesses. Each was read out of the Hex-Rays decompilation of GameAssembly.dll
    /// and cross-checked against an early Mono build of the same game. Three independent functions
    /// index the input array the same way (`_VD._pz`, `_VD._Uz`, `_tD._Az`), which is what pins that
    /// layout down.
    /// </para>
    /// <para>
    /// The values written here are the ones <b>this build</b> of the game was reversed with. They are
    /// defaults, not the truth: <see cref="Resolve"/> asks the running game for the real offset of
    /// every field it can and overwrites them, so a game update that recompiles the game and shifts
    /// its fields does not have to be re-reversed. That covers most of what is below.
    /// </para>
    /// <para>
    /// What it cannot cover, and why: a value type's <b>size</b> and the layout of IL2CPP's own
    /// containers are not fields, so there is nothing to look up by name —
    /// <see cref="Note"/>.Size, <see cref="Judgement"/>.Size and everything in
    /// <see cref="Runtime"/> stay build constants. (The containers are IL2CPP's layout rather than
    /// the game's, so a game patch does not move them.) One field is also out of reach:
    /// <see cref="NotePlayer"/>.NoteList, because it is not a field of the player but the third slot
    /// of a <c>ValueTuple</c> that is.
    /// </para>
    /// <para>
    ///     Engine       _VD            the gameplay engine
    ///     SafeArea     _Ae            the shield / safe-area struct embedded in _VD
    ///     InputLane    one lane entry  what _Pz writes and _pz consumes
    ///     Note         _fA            a note, 128 bytes, read as an interior pointer
    ///     Plane        _CA            which plane a note is on
    ///     Judgement    _cH            a judgement group, where the grade lands
    ///     Track        Track          the scene object that draws the sky strip
    ///     NotePlayer   LogicalNotePlayer  the per-frame note culler
    ///     ChartObject  _T             the loaded chart and its note holders
    ///     PlaneHolder  _hA            one plane's note array
    ///     Runtime      .NET containers  List / Dictionary / Memory, as IL2CPP lays them out
    /// </para>
    /// </remarks>
    internal static class Offsets
    {
        /// <summary>_VD — the gameplay engine. All offsets are _VD-relative.</summary>
        internal static class Engine
        {
            /// <summary>float — the sky cursor's raw X accumulator. `_VD._iz()` returns it, and
            /// `_VD._Mz` adds the mouse delta to it. Unclamped: it can sit outside 0..1.</summary>
            internal static int CursorRaw = 0x10;          // _oEA

            /// <summary>
            /// float — the same value clamped to 0..1 (`_tEA`). This is the one `_VD._Oz` actually
            /// scores against: it reads +0x98, not +0x10, when it works out the X interval the sky
            /// cursor covers. `_VD._Mz` writes it from +0x10 on every mouse move.
            /// </summary>
            internal static int CursorClamped = 0x98;      // _tEA

            /// <summary>double — current chart time in seconds. `_VD._kz(double)` writes it.</summary>
            internal static int ChartTime = 0xD0;          // _WEA

            /// <summary>_tD — the input array holder. `a1[11]` in _pz, `[rsi+58h]` in _Uz.</summary>
            internal static int Input = 0x58;              // _pEA
        }

        /// <summary>
        /// _Ae, the safe-area / shield state at _VD+0x18 (a 64-byte struct).
        ///
        /// The offsets below are _VD-relative, i.e. _Ae's own offsets plus 0x18. _Ae is a struct
        /// embedded in _VD (`private _Ae _OEA; // 0x18`, next field at 0x58, the struct 0x40 bytes),
        /// so _VD+0x18 is _Ae._OfA itself rather than a pointer to it. Resolving one of these takes
        /// two lookups for that reason: the struct's offset on _VD, then the field's offset in _Ae.
        ///
        /// A sky bar's grade turns on three of these, and every read and write of all three in the
        /// whole binary is inside `_VD._Oz` and its timer `_VD._oz` — see <see cref="SkyBar"/>.
        /// Nothing in Track reads them, so pinning them changes the judgement and nothing else.
        /// </summary>
        internal static class SafeArea
        {
            /// <summary>double, 0..300 — safe duration left. Decays twice as fast off the bar.</summary>
            internal static int SafeDuration = 0x18;       // _OEA._OfA

            /// <summary>double, 0..500 — forced-miss timer. Anything above 0 grades the bar Miss.</summary>
            internal static int ForcedMiss = 0x20;         // _OEA._pfA

            /// <summary>long — highest group id seen; the bar's start sample is taken when it advances.</summary>
            internal static int GroupMark = 0x40;          // _OEA._rfA

            /// <summary>long — highest note id seen; advancing it resets the start sample to false.</summary>
            internal static int NoteMark = 0x48;           // _OEA._RfA

            /// <summary>bool — a note is inside its window this tick.</summary>
            internal static int InWindow = 0x50;           // _OEA._sfA

            /// <summary>bool — the cursor is inside the bar's moving range this tick.</summary>
            internal static int OnBar = 0x51;              // _OEA._SfA

            /// <summary>
            /// bool — the cursor was inside the bar's start range when it opened. False makes
            /// `_VD._Oz` skip the bar's tick entirely; it is only ever graded once the bar has
            /// ended, and then as one Miss for the whole bar.
            /// </summary>
            internal static int StartOk = 0x52;            // _OEA._tfA

            /// <summary>The value `_VD._oz` refills SafeDuration with when the player is on the bar.</summary>
            internal const double SafeDurationFull = 300.0;

            /// <summary>The value `_VD._oz` raises ForcedMiss to when the safe duration runs out.</summary>
            internal const double ForcedMissValue = 500.0;
        }

        /// <summary>
        /// One entry of the input array — `data + lane*Stride`, the data pointer being at array+0x20.
        /// `_VD._Pz` writes the press, `sub_1805F8A80` the release, `_tD._Az` clears the per-tick
        /// flags, and `_VD._pz` / `_VD._Uz` read all of it.
        ///
        /// The entry is `_zD`, reached through `_tD._BEA` — the holder is itself a struct, and the
        /// array is its only field. The five field offsets resolve like any other; <see cref="Stride"/>
        /// is a size and is measured from the array (see <see cref="FieldResolver.ElementSize"/>).
        /// </summary>
        internal static class InputLane
        {
            /// <summary>Bytes per lane entry — `sizeof(_zD)`. `lane*24` throughout the decompilation.</summary>
            internal static int Stride = 24;

            /// <summary>Element + 0: bool, the press edge. Cleared by `_tD._Az()` before every
            /// tick. This is the one the judgement eats.</summary>
            internal static int PressEdge = 0;      // _lfA

            /// <summary>
            /// Element + 1: bool, "held". Set on key down, cleared on key up, and — unlike the edge
            /// flag — never cleared per tick. `_Uz` ORs this across a note's lanes.
            /// </summary>
            internal static int Held = 1;           // _LfA

            /// <summary>Element + 2: bool, the press has been consumed by a note this tick.</summary>
            internal static int Consumed = 2;       // _mfA

            /// <summary>Element + 8: double, the judge time in stopwatch seconds.</summary>
            internal static int JudgeTime = 8;      // _MfA

            /// <summary>Element + 16: double, the time of the last input on this lane.</summary>
            internal static int LastInput = 16;     // _nfA
        }

        /// <summary>
        /// _fA — a note, 128 bytes.
        ///
        /// A note reaches the mod as an `in _fA`: an interior pointer into an `_fA[]`, which
        /// `_VD._Oz` computes as `notes_base + i * 128` (0x1805F4CA6). It is not an IL2CPP object
        /// and must not be validated as one — see <see cref="Memory.LooksLikeNote"/>.
        ///
        /// The four geometry values are not fields of _fA at all: each is the `_LD` inside a 12-byte
        /// `_BA` at 0x20, 0x2C, 0x38 and 0x44, so resolving one takes two lookups.
        /// </summary>
        internal static class Note
        {
            /// <summary>
            /// sizeof(_fA) as laid out by the IL2CPP compiler — the stride of the note array. A size,
            /// so it is measured from a live array rather than looked up by name.
            /// </summary>
            internal static int Size = 128;

            internal static int Id = 0x00;          // _ZD   long
            internal static int GroupId = 0x08;     // _ae   long, key into the _cH dictionary
            internal static int Side = 0x10;        // _Ae   _CA  0=none 1=MAIN 2=SHIFT 3=SPACE 4=SKY
            internal static int Type = 0x14;        // _be   _HA  note kind
            internal static int StartMs = 0x18;     // _Be   int, absolute chart time
            internal static int EndMs = 0x1C;       // _ce   int, == StartMs for a tap
            internal static int StartX = 0x28;      // _Ce._LD  float, the note's centre at progress 0
            internal static int EndX = 0x34;        // _de._LD  float
            internal static int StartWidth = 0x40;  // _De._LD  float
            internal static int EndWidth = 0x4C;    // _ee._LD  float
            internal static int DeltaMs = 0x50;     // _Ee   double, ms until the note — recomputed every frame
            internal static int Judged = 0x70;      // _Ge   bool
            internal static int Flags = 0x74;       // _he   _FA  per-note modifiers, incl. the sky-bar easings
            internal static int LaneFirst = 0x78;   // _He   int
            internal static int LaneLast = 0x7C;    // _ie   int
        }

        /// <summary>_CA — which plane a note belongs to.</summary>
        internal static class Plane
        {
            internal const int None = 0;
            internal const int Main = 1;
            internal const int Shift = 2;
            internal const int Space = 3;
            internal const int Sky = 4;

            /// <summary>Lane 0 is SHIFT, 1..4 are the floor lanes, 5 is SPACE — see Track.$FV.</summary>
            internal const int LaneCount = 6;
        }

        /// <summary>
        /// _cH — a judgement group, where the game writes the grade. Reached through `_T._XC`, a
        /// `Dictionary&lt;long, Memory&lt;_cH&gt;&gt;` keyed by note id.
        /// </summary>
        internal static class Judgement
        {
            /// <summary>
            /// sizeof(_cH) including padding — `40 * i` in _pz and _nz. A size, so it is measured from
            /// a live array rather than looked up by name.
            /// </summary>
            internal static int Size = 40;

            /// <summary>double — the group's own offset within the note, added to _fA._Ee.</summary>
            internal static int OffsetMs = 0x08;    // _udb

            /// <summary>_FH — the grade. Values in <see cref="Grade"/>.</summary>
            internal static int Grade = 0x20;       // _wdb

            /// <summary>bool — the group has been judged.</summary>
            internal static int Judged = 0x21;      // _Wdb
        }

        /// <summary>Track — the scene object that draws the sky strip. Offsets are Track-relative.</summary>
        internal static class Track
        {
            /// <summary>
            /// Transform — `skyCursor`, the sky judgement point: the object a human drags along the
            /// strip with the mouse. This is the thing autoplay removes.
            /// </summary>
            internal static int SkyCursor = 0x170;              // skyCursor

            /// <summary>MeshRenderer — the same object's renderer, cached separately by Track.</summary>
            internal static int SkyCursorRenderer = 0x178;      // skyCursorMeshRenderer

            /// <summary>Transform — `skyGhostCursor`, shown instead of the cursor while the raw
            /// cursor value is outside 0..1, i.e. while the mouse is off the end of the strip.</summary>
            internal static int SkyGhostCursor = 0x1A0;         // skyGhostCursor

            /// <summary>Transform — `SkyCursorExtrasContainer`, decoration that follows the cursor.</summary>
            internal static int CursorExtras = 0x1A8;           // cursorExtrasContainer

            /// <summary>
            /// _VD — the gameplay engine the track is drawing. `Track._ZbA(_VD)` binds it, and
            /// `Track._FBA` reads it to decide the ghost cursor's visibility.
            /// </summary>
            internal static int Engine = 0x830;                 // _YnA
        }

        /// <summary>LogicalNotePlayer — the once-per-frame note culler, and the way in to the chart.</summary>
        internal static class NotePlayer
        {
            /// <summary>
            /// `List&lt;_fA&gt;` — the loaded chart's notes. This is the cross-check path only; the
            /// chart itself is read the way the game reads it, through `_T._vC`.
            ///
            /// Not a field of the player: it is the second item of the
            /// `ValueTuple&lt;_t, List&lt;_fA&gt;, List&lt;_eA&gt;&gt;` that `_Ue` holds, so reaching it takes
            /// the tuple's class as well — see <see cref="FieldResolver.TupleItem"/>.
            /// </summary>
            internal static int NoteList = 0x30;

            /// <summary>
            /// `_T` — the loaded chart. `*(lnp + 0x40)`. Its field list lines up with every accessor
            /// on LogicalNotePlayer: `_Hb()` returns +0x10, `_ib()` +0x38, `_Tb()` +0x60, `_vb()` +0x70.
            /// </summary>
            internal static int ChartObject = 0x40;   // _ve
        }

        /// <summary>_T — the loaded chart. Offsets are _T-relative.</summary>
        internal static class ChartObject
        {
            /// <summary>_T._vC — Dictionary&lt;_CA, _hA&gt;, the per-plane note holders.</summary>
            internal static int PlaneArrays = 0x10;   // _vC

            /// <summary>_T._XC — Dictionary&lt;long, Memory&lt;_cH&gt;&gt;, the judgement groups. `_ib()`.</summary>
            internal static int GroupDict = 0x38;     // _XC
        }

        /// <summary>_hA — one plane's notes, reached as an entry value of `_T._vC`.</summary>
        internal static class PlaneHolder
        {
            /// <summary>_hA._ye — the plane's `_fA[]`.</summary>
            internal static int NoteArray = 0x10;     // _ye

            /// <summary>_hA._ze — how many of that array are live.</summary>
            internal static int Count = 0x28;         // _ze
        }

        /// <summary>
        /// The .NET containers the game hands out, as IL2CPP lays them out.
        ///
        /// Constants, and not because they were missed: these are the runtime's own layout rather
        /// than the game's fields, so there is nothing in the game's metadata to look them up by —
        /// and, being the runtime's, a game patch does not move them.
        /// </summary>
        internal static class Runtime
        {
            /// <summary>IL2CPP arrays start their elements here (klass+monitor+bounds+length).</summary>
            internal const int ArrayDataOffset = 0x20;

            /// <summary>List&lt;T&gt;._items.</summary>
            internal const int ListItems = 0x10;

            /// <summary>List&lt;T&gt;._size — and the IL2CPP array length when applied to an array.</summary>
            internal const int ListSize = 0x18;

            // .NET Dictionary internals. `_buckets` and `_entries` are the first two fields in
            // every version; the entry stride for <int, reference> is hashCode/next/key/pad/value.
            internal const int DictEntries = 0x18;
            internal const int DictCount = 0x20;
            internal const int DictEntrySize = 24;
            internal const int DictEntryKey = 8;
            internal const int DictEntryValue = 16;

            /// <summary>Entry stride and field offsets for Dictionary&lt;long, Memory&lt;_cH&gt;&gt;.</summary>
            internal const int GroupEntrySize = 32;
            internal const int GroupEntryKey = 8;
            internal const int GroupEntryValue = 16;

            /// <summary>Memory&lt;T&gt; is object / index / length.</summary>
            internal const int MemoryObject = 0;
            internal const int MemoryIndex = 8;
        }

        /// <summary>
        /// Asks the running game for the offset of every field above that is one, and keeps the
        /// value written here for the rest.
        ///
        /// Called once, before any hook is installed. Each lookup is independent, so a game update
        /// that moves some fields and not others is handled field by field rather than all at once —
        /// and one that cannot be resolved at all leaves the build's own value in place, which is
        /// the behaviour without this method.
        /// </summary>
        internal static void Resolve()
        {
            Engine.CursorRaw = FieldResolver.Field("_VD", "_oEA", Engine.CursorRaw);
            Engine.CursorClamped = FieldResolver.Field("_VD", "_tEA", Engine.CursorClamped);
            Engine.ChartTime = FieldResolver.Field("_VD", "_WEA", Engine.ChartTime);
            Engine.Input = FieldResolver.Field("_VD", "_pEA", Engine.Input);

            SafeArea.SafeDuration = FieldResolver.Embedded("_VD", "_OEA", "_Ae", "_OfA", SafeArea.SafeDuration);
            SafeArea.ForcedMiss = FieldResolver.Embedded("_VD", "_OEA", "_Ae", "_pfA", SafeArea.ForcedMiss);
            SafeArea.GroupMark = FieldResolver.Embedded("_VD", "_OEA", "_Ae", "_rfA", SafeArea.GroupMark);
            SafeArea.NoteMark = FieldResolver.Embedded("_VD", "_OEA", "_Ae", "_RfA", SafeArea.NoteMark);
            SafeArea.InWindow = FieldResolver.Embedded("_VD", "_OEA", "_Ae", "_sfA", SafeArea.InWindow);
            SafeArea.OnBar = FieldResolver.Embedded("_VD", "_OEA", "_Ae", "_SfA", SafeArea.OnBar);
            SafeArea.StartOk = FieldResolver.Embedded("_VD", "_OEA", "_Ae", "_tfA", SafeArea.StartOk);

            Note.Id = FieldResolver.Field("_fA", "_ZD", Note.Id);
            Note.GroupId = FieldResolver.Field("_fA", "_ae", Note.GroupId);
            Note.Side = FieldResolver.Field("_fA", "_Ae", Note.Side);
            Note.Type = FieldResolver.Field("_fA", "_be", Note.Type);
            Note.StartMs = FieldResolver.Field("_fA", "_Be", Note.StartMs);
            Note.EndMs = FieldResolver.Field("_fA", "_ce", Note.EndMs);
            Note.StartX = FieldResolver.Embedded("_fA", "_Ce", "_BA", "_LD", Note.StartX);
            Note.EndX = FieldResolver.Embedded("_fA", "_de", "_BA", "_LD", Note.EndX);
            Note.StartWidth = FieldResolver.Embedded("_fA", "_De", "_BA", "_LD", Note.StartWidth);
            Note.EndWidth = FieldResolver.Embedded("_fA", "_ee", "_BA", "_LD", Note.EndWidth);
            Note.DeltaMs = FieldResolver.Field("_fA", "_Ee", Note.DeltaMs);
            Note.Judged = FieldResolver.Field("_fA", "_Ge", Note.Judged);
            Note.Flags = FieldResolver.Field("_fA", "_he", Note.Flags);
            Note.LaneFirst = FieldResolver.Field("_fA", "_He", Note.LaneFirst);
            Note.LaneLast = FieldResolver.Field("_fA", "_ie", Note.LaneLast);

            Judgement.OffsetMs = FieldResolver.Field("_cH", "_udb", Judgement.OffsetMs);
            Judgement.Grade = FieldResolver.Field("_cH", "_wdb", Judgement.Grade);
            Judgement.Judged = FieldResolver.Field("_cH", "_Wdb", Judgement.Judged);

            Track.SkyCursor = FieldResolver.Field("Track", "skyCursor", Track.SkyCursor);
            Track.SkyCursorRenderer = FieldResolver.Field("Track", "skyCursorMeshRenderer", Track.SkyCursorRenderer);
            Track.SkyGhostCursor = FieldResolver.Field("Track", "skyGhostCursor", Track.SkyGhostCursor);
            Track.CursorExtras = FieldResolver.Field("Track", "cursorExtrasContainer", Track.CursorExtras);
            Track.Engine = FieldResolver.Field("Track", "_YnA", Track.Engine);

            NotePlayer.ChartObject = FieldResolver.Field("LogicalNotePlayer", "_ve", NotePlayer.ChartObject);

            ChartObject.PlaneArrays = FieldResolver.Field("_T", "_vC", ChartObject.PlaneArrays);
            ChartObject.GroupDict = FieldResolver.Field("_T", "_XC", ChartObject.GroupDict);

            PlaneHolder.NoteArray = FieldResolver.Field("_hA", "_ye", PlaneHolder.NoteArray);
            PlaneHolder.Count = FieldResolver.Field("_hA", "_ze", PlaneHolder.Count);

            InputLane.PressEdge = FieldResolver.Field("_zD", "_lfA", InputLane.PressEdge);
            InputLane.Held = FieldResolver.Field("_zD", "_LfA", InputLane.Held);
            InputLane.Consumed = FieldResolver.Field("_zD", "_mfA", InputLane.Consumed);
            InputLane.JudgeTime = FieldResolver.Field("_zD", "_MfA", InputLane.JudgeTime);
            InputLane.LastInput = FieldResolver.Field("_zD", "_nfA", InputLane.LastInput);

            // Not here: the three sizes are measured where a live array of the type exists
            // (InputLane.Stride in Memory.InputLanes, Note.Size and Judgement.Size in Chart), because
            // a size has no name and an array is the only thing that carries it.
            NotePlayer.NoteList = FieldResolver.TupleItem("LogicalNotePlayer", "_Ue", "Item2",
                                                          NotePlayer.NoteList);
        }
    }
}
