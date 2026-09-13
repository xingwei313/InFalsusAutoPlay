using System;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// The raw-memory layer: every read and write of game memory in the mod goes through here, so
    /// "what does this mod touch" is answerable by reading one file.
    ///
    /// Nothing here decides anything about gameplay. The offsets it adds to live in
    /// <see cref="Offsets"/>, and the callers that know what a value means live in
    /// Gameplay/Chart, Gameplay/Floor and Gameplay/Sky.
    ///
    /// The primitive loads are deliberately unchecked — a bad pointer is an access violation, which
    /// no `try`/`catch` in this mod can catch, so the checking happens before the read, in
    /// <see cref="LooksLikeObject"/> and <see cref="LooksLikeNote"/>, and callers are expected to
    /// ask first.
    /// </summary>
    internal static unsafe class Memory
    {
        // ---------------------------------------------------------------- reads and writes

        internal static float F32(IntPtr p) => *(float*)p;
        internal static double F64(IntPtr p) => *(double*)p;
        internal static int I32(IntPtr p) => *(int*)p;
        internal static long I64(IntPtr p) => *(long*)p;
        internal static byte U8(IntPtr p) => *(byte*)p;
        internal static IntPtr Ptr(IntPtr p) => *(IntPtr*)p;

        internal static void WriteF32(IntPtr p, float v) => *(float*)p = v;
        internal static void WriteF64(IntPtr p, double v) => *(double*)p = v;
        internal static void WriteU8(IntPtr p, byte v) => *(byte*)p = v;

        // ---------------------------------------------------------------- is this pointer safe

        /// <summary>True when a pointer looks like a live IL2CPP object rather than garbage.</summary>
        internal static bool LooksLikeObject(IntPtr p)
        {
            if (p == IntPtr.Zero) return false;
            long v = p.ToInt64();
            // User-mode addresses on Win64 stay below 0x0000_8000_0000_0000 and objects are
            // 8-byte aligned; the klass pointer at +0 has to be in the same range.
            if (v < 0x10000 || v > 0x00007FFFFFFFFFFF || (v & 7) != 0) return false;
            long klass = *(long*)p;
            return klass > 0x10000 && klass < 0x00007FFFFFFFFFFF;
        }

        /// <summary>
        /// True when a pointer looks like a note the game handed out, rather than garbage.
        ///
        /// This is deliberately not <see cref="LooksLikeObject"/>. A note reaches the mod as
        /// `in _fA` — an interior pointer into an `_fA[]`, computed by the game as
        /// `notes_base + i * 128` (`_VD._Oz` at 0x1805F4CA6). Its first eight bytes are the note
        /// id, not a klass pointer, so testing it as an object makes the check depend on the id
        /// being at least 0x10000 — which silently disables whatever called it on any chart whose
        /// ids are small. That is exactly the trap `Sky.AimAt` has to avoid, so the distinction is
        /// load-bearing.
        ///
        /// What can be checked without knowing the id range is that the pointer is non-null and
        /// aligned, and that the fields the caller is about to read are in range.
        /// </summary>
        internal static bool LooksLikeNote(IntPtr p)
        {
            long v = p.ToInt64();
            if (v < 0x10000 || v > 0x00007FFFFFFFFFFF || (v & 7) != 0) return false;

            int side = I32(p + Offsets.Note.Side);
            if (side < Offsets.Plane.None || side > Offsets.Plane.Sky) return false;

            int type = I32(p + Offsets.Note.Type);
            return type >= 0 && type <= 8;
        }

        // ---------------------------------------------------------------- the input array

        /// <summary>Address of the input array's first element, or Zero if it is not usable.</summary>
        internal static IntPtr InputLanes(IntPtr engine)
        {
            if (!LooksLikeObject(engine)) return IntPtr.Zero;

            IntPtr array = Ptr(engine + Offsets.Engine.Input);
            if (!LooksLikeObject(array)) return IntPtr.Zero;

            int len = I32(array + Offsets.Runtime.ListSize);
            if (len < Offsets.Plane.LaneCount || len > 64) return IntPtr.Zero;

            // The one moment an `_zD` array is in hand, so the one moment its element size can be
            // measured rather than assumed.
            Offsets.InputLane.Stride = FieldResolver.ElementSize(array, "InputLane.Stride",
                                                                 Offsets.InputLane.Stride);

            return array + Offsets.Runtime.ArrayDataOffset;
        }

        /// <summary>One lane's entry in that array.</summary>
        internal static IntPtr Lane(IntPtr lanes, int lane) => lanes + lane * Offsets.InputLane.Stride;
    }
}
