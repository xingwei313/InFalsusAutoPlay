using System;
using System.Collections.Generic;
using System.Reflection;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// Finds a game field's offset at runtime, so a game update that recompiles the game and shifts
    /// every field does not have to be re-reversed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the same route <see cref="MethodResolver"/> takes for methods, and the same route the
    /// interop layer's own generated code takes: the generated type carries a static field per game
    /// field (<c>NativeFieldInfoPtr_&lt;name&gt;</c>) holding that field's FieldInfo pointer, and
    /// IL2CPP will say what offset it has. Nothing here is inferred — the name comes from the
    /// metadata and the offset comes from the runtime.
    /// </para>
    /// <para>
    /// A <b>value type's</b> fields are reported relative to the boxed object, which carries the
    /// two-pointer header every IL2CPP object has, so the header comes back off. That is not a
    /// detail to be reasoned about: it was measured, and every value-type field the mod reads came
    /// back exactly that much further along while no class field came back shifted at all. Getting it
    /// wrong is not subtle — the mod reads every note at the wrong offset and autoplay stops.
    /// </para>
    /// <para>
    /// What it does <b>not</b> do is guess. When a value cannot be had at all, the one the caller was
    /// written with is kept, and that is the only case where this class changes nothing.
    /// </para>
    /// </remarks>
    internal static class FieldResolver
    {
        private const BindingFlags AnyMember =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        private static int _agreed;
        private static int _moved;
        private static int _failed;

        /// <summary>How many offsets agreed, moved, or could not be had. For the status line.</summary>
        internal static string Stats() =>
            $"offsets agreed={_agreed} moved={_moved} unresolved={_failed}";

        // ---------------------------------------------------------------- fields

        /// <summary>
        /// The offset of <paramref name="fieldName"/> on <paramref name="typeName"/>, or
        /// <paramref name="fallback"/> if it cannot be had.
        /// </summary>
        internal static int Field(string typeName, string fieldName, int fallback)
        {
            if (!TryField(typeName, fieldName, out int offset)) return Missed(typeName, fieldName, fallback);

            return Adopt($"{typeName}.{fieldName}", offset, fallback);
        }

        /// <summary>
        /// A field of a struct embedded in a class rather than pointed at by it — the safe-area state
        /// is one: <c>_VD._OEA</c> is the struct itself, so a field's offset is the class-relative
        /// offset of the struct plus the struct-relative offset of the field.
        /// </summary>
        internal static int Embedded(string typeName, string structField, string structType,
                                     string fieldName, int fallback)
        {
            if (!TryField(typeName, structField, out int outer) ||
                !TryField(structType, fieldName, out int inner))
            {
                return Missed(typeName, $"{structField}.{fieldName}", fallback);
            }

            return Adopt($"{typeName}.{structField}.{fieldName}", outer + inner, fallback);
        }

        /// <summary>
        /// An item of a tuple that is a field of a class — the second slot of
        /// <c>LogicalNotePlayer._Ue</c>, which is where the note list hangs. The item is not a field
        /// of the player, so it takes two lookups through the tuple's own class, and that class is
        /// only reachable from the field's type at runtime.
        /// </summary>
        internal static int TupleItem(string typeName, string tupleField, string itemName, int fallback)
        {
            if (!TryField(typeName, tupleField, out int outer)) return Missed(typeName, tupleField, fallback);

            IntPtr tuple = TupleClass(typeName, tupleField);
            if (tuple == IntPtr.Zero) return Missed(typeName, $"{tupleField}.{itemName}", fallback);

            IntPtr item = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(tuple, itemName);
            if (item == IntPtr.Zero) return Missed(typeName, $"{tupleField}.{itemName}", fallback);

            int inner = Reported(item, tuple);
            return Adopt($"{typeName}.{tupleField}.{itemName}", outer + inner, fallback);
        }

        /// <summary>
        /// A field's offset with nothing to compare it against — for one that is only used to bracket
        /// something else, where a fallback would be a made-up number rather than a known one.
        /// </summary>
        internal static int Lookup(string typeName, string fieldName) =>
            TryField(typeName, fieldName, out int offset) ? offset : -1;

        /// <summary>
        /// The class pointer for a generated type, so a class can be reached by name rather than
        /// through the address of the slot that happens to hold it.
        /// </summary>
        internal static IntPtr ClassPointer(string typeName)
        {
            if (!InteropTypeIndex.ByName().TryGetValue(typeName, out List<Type> types))
                return IntPtr.Zero;

            foreach (Type type in types)
            {
                try
                {
                    FieldInfo pointer = typeof(Il2CppInterop.Runtime.Il2CppClassPointerStore<>)
                        .MakeGenericType(type)
                        .GetField("NativeClassPtr", BindingFlags.Public | BindingFlags.Static);

                    if (pointer == null) continue;

                    IntPtr klass = (IntPtr)pointer.GetValue(null);
                    if (klass != IntPtr.Zero) return klass;
                }
                catch (Exception e)
                {
                    Diagnostics.Warn($"{typeName} class pointer could not be read: " +
                                     Diagnostics.Describe(e));
                }
            }
            return IntPtr.Zero;
        }

        // ---------------------------------------------------------------- sizes

        /// <summary>
        /// The size of an IL2CPP array's elements, taken from the array itself.
        ///
        /// A value type's size is not a field, so it has nothing to look up by name — but an array
        /// of them carries it: its byte length is its element count times the element size. That is
        /// the one piece of the mod that a game update changing a struct's shape would still catch.
        /// </summary>
        internal static int ElementSize(IntPtr array, string label, int fallback)
        {
            if (Measured.TryGetValue(label, out int known)) return known;

            int size = fallback;
            try
            {
                if (Memory.LooksLikeObject(array))
                {
                    uint length = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_length(array);
                    uint bytes = length == 0
                        ? 0
                        : Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(array);

                    if (length > 0 && length <= 1_000_000 && bytes > 0 && bytes % length == 0)
                        size = Adopt(label, (int)(bytes / length), fallback);
                }
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"{label} could not be measured: {Diagnostics.Describe(e)}");
                _failed++;
            }

            Measured[label] = size;
            return size;
        }

        private static readonly Dictionary<string, int> Measured =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// The size of something the two fields around it bracket — the block copied out of a text,
        /// which starts where one field starts and ends where the next one does.
        /// </summary>
        internal static int Between(string label, int from, int to, int fallback)
        {
            if (from < 0 || to <= from)
            {
                _failed++;
                Diagnostics.Warn($"could not measure {label}; keeping 0x{fallback:X}");
                return fallback;
            }

            return Adopt(label, to - from, fallback);
        }

        // ---------------------------------------------------------------- taking an answer

        /// <summary>
        /// Takes the game's answer, reporting it when it is not the one this build was reversed with.
        /// </summary>
        /// <remarks>
        /// This is the point of the class, so the answer is taken rather than compared — but the
        /// comparison is still made and reported, because that line is the only way to tell "the game
        /// moved this field" from "the mod is reading the wrong place". Every name that reaches here
        /// is one a run has already shown to resolve to the offset it is meant to.
        /// </remarks>
        private static int Adopt(string label, int resolved, int fallback)
        {
            if (resolved == fallback)
            {
                _agreed++;
                return fallback;
            }

            _moved++;
            Diagnostics.Info($"{label} has moved to 0x{resolved:X} " +
                             $"(this build was reversed with 0x{fallback:X}); using 0x{resolved:X}");
            return resolved;
        }

        private static int Missed(string typeName, string fieldName, int fallback)
        {
            _failed++;
            Diagnostics.Warn($"could not resolve {typeName}.{fieldName}; keeping the offset this " +
                             $"build was reversed with (0x{fallback:X})");
            return fallback;
        }

        // ---------------------------------------------------------------- plumbing

        private static bool TryField(string typeName, string fieldName, out int offset)
        {
            offset = 0;

            try
            {
                if (!InteropTypeIndex.ByName().TryGetValue(typeName, out List<Type> types))
                    return false;

                foreach (Type type in types)
                {
                    FieldInfo pointer = PointerField(type, fieldName);
                    if (pointer == null) continue;

                    IntPtr info = (IntPtr)pointer.GetValue(null);
                    if (info == IntPtr.Zero) continue;

                    offset = type.IsValueType
                        ? (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(info) - ObjectHeader
                        : (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(info);
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"{typeName}.{fieldName} could not be resolved: " +
                                 Diagnostics.Describe(e));
                return false;
            }
        }

        /// <summary>
        /// The two pointers every IL2CPP object starts with — its class and its monitor. A value
        /// type's field offsets are reported relative to that header, because that is where they sit
        /// once the struct is boxed; the offsets the mod uses are relative to the struct itself.
        /// </summary>
        private const int ObjectHeader = 0x10;

        /// <summary>A field's offset on an already-found class, correcting for that class being a value type.</summary>
        private static int Reported(IntPtr field, IntPtr klass) =>
            (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(field) -
            (IsValueType(klass) ? ObjectHeader : 0);

        /// <summary>
        /// Whether a class is a value type. There is no <c>System.Type</c> to ask here — the class came
        /// from IL2CPP — so it is answered the way IL2CPP does: a value type's parent is
        /// <c>System.ValueType</c>.
        /// </summary>
        private static bool IsValueType(IntPtr klass)
        {
            IntPtr parent = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_parent(klass);
            if (parent == IntPtr.Zero) return false;

            return Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(parent) == "ValueType";
        }

        /// <summary>The class of a field's type, for reaching something the field points into.</summary>
        private static IntPtr TupleClass(string typeName, string fieldName)
        {
            if (!InteropTypeIndex.ByName().TryGetValue(typeName, out List<Type> types))
                return IntPtr.Zero;

            foreach (Type type in types)
            {
                FieldInfo pointer = PointerField(type, fieldName);
                if (pointer == null) continue;

                IntPtr info = (IntPtr)pointer.GetValue(null);
                if (info == IntPtr.Zero) continue;

                IntPtr il2cppType = Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_type(info);
                if (il2cppType == IntPtr.Zero) continue;

                return Il2CppInterop.Runtime.IL2CPP.il2cpp_class_from_type(il2cppType);
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// The generated field holding a game field's FieldInfo pointer.
        ///
        /// Two routes, because the first depends on the generator emitting a property for the field
        /// and the second on how it names the pointer field. Either alone would do on this build;
        /// together they survive a change in the other.
        /// </summary>
        private static FieldInfo PointerField(Type type, string fieldName)
        {
            PropertyInfo property = type.GetProperty(fieldName, AnyMember);
            MethodInfo getter = property?.GetGetMethod(true);
            if (getter != null)
            {
                FieldInfo viaAccessor = Il2CppInterop.Common.Il2CppInteropUtils
                    .GetIl2CppFieldInfoPointerFieldForGeneratedFieldAccessor(getter);
                if (viaAccessor != null) return viaAccessor;
            }

            return type.GetField("NativeFieldInfoPtr_" + fieldName, AnyMember);
        }
    }
}
