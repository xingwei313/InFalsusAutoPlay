using System;
using System.Collections.Generic;
using System.Reflection;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// Finds the native entry point of an IL2CPP method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two routes, because neither is reliable on its own:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// The Cpp2IL interop assembly keeps a hidden static field per method holding the runtime
    /// MethodInfo*, whose first field is the method pointer. This survives game updates that move
    /// functions around, but the field is only filled in once the method has been reached by
    /// il2cpp_codegen_initialize_method.
    /// </description></item>
    /// <item><description>
    /// The RVA from dump.cs, relocated against the real module base. Pinned to this build.
    /// </description></item>
    /// </list>
    /// <para>
    /// A name is not enough when the method is overloaded. The MethodInfo route takes the first
    /// method it finds with that name, so for anything with two or more overloads — the
    /// `FastText.SetText` family, for one — it can return a different overload than the one meant,
    /// and the call that follows will be wrong in a way nothing reports. Such a method goes through
    /// <see cref="BySignature"/> instead, which adds a parameter type to the name; <see cref="ByRva"/>
    /// is the last resort, for something that is not in the metadata at all.
    /// </para>
    /// </remarks>
    internal static unsafe class MethodResolver
    {
        private const BindingFlags AnyMethod =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        /// <summary>
        /// A method's native address: the runtime MethodInfo if it can be found, otherwise the RVA.
        /// Zero when neither works.
        /// </summary>
        internal static IntPtr ByName(string typeName, string methodName, long rva)
        {
            IntPtr p = ViaMethodInfo(typeName, methodName);
            if (p != IntPtr.Zero)
            {
                Diagnostics.Info($"{typeName}.{methodName} -> 0x{p.ToInt64():X} (MethodInfo)");
                return p;
            }

            p = GameAssembly.FromRva(rva);
            if (p == IntPtr.Zero)
            {
                Diagnostics.Warn($"could not resolve {typeName}.{methodName}");
                return IntPtr.Zero;
            }

            Diagnostics.Warn($"{typeName}.{methodName} -> 0x{p.ToInt64():X} (RVA 0x{rva:X}; " +
                             "the game may have been patched since this RVA was read)");
            return p;
        }

        /// <summary>
        /// A native address for a function that has no managed method to name it.
        ///
        /// The layout system's internals are like this: the helpers `Constrained2D.Reset` calls are
        /// real functions with a stable ABI, but they are not entries in the metadata, so there is
        /// no MethodInfo to prefer and nothing to fall back from. Callers of this must already
        /// have read the argument list off the decompilation — the same rule as a hook, for the
        /// same reason.
        /// </summary>
        internal static IntPtr ByRva(long rva, string label)
        {
            IntPtr p = GameAssembly.FromRva(rva);
            if (p == IntPtr.Zero)
                Diagnostics.Warn($"could not resolve {label} (RVA 0x{rva:X}); is GameAssembly.dll loaded?");
            else
                Diagnostics.Info($"{label} -> 0x{p.ToInt64():X} (RVA 0x{rva:X})");
            return p;
        }

        /// <summary>
        /// A method by name <b>and</b> the type of its first parameter — for the ones a name alone
        /// cannot identify.
        ///
        /// `FastText` is the reason this exists: it has three `SetText` overloads that all take one
        /// argument and an update type, and two `SetTextNonLocalized` ones, so "the first method of
        /// that name" is a coin toss. Their first parameters are all different —
        /// `Strings.Key`, `TextParameters` and `DynamicString` for `SetText`, `string` and
        /// `ReadOnlySpan&lt;char&gt;` for `SetTextNonLocalized` — so the parameter settles it.
        ///
        /// The match is by name prefix, because a by-ref parameter reports as `X&amp;` and the
        /// declaring assembly may prefix the namespace.
        /// </summary>
        internal static IntPtr BySignature(string typeName, string methodName, string firstParameter,
                                           long rva, string label)
        {
            IntPtr p = ViaMethodInfo(typeName, methodName, firstParameter);
            if (p != IntPtr.Zero)
            {
                Diagnostics.Info($"{label} -> 0x{p.ToInt64():X} (MethodInfo)");
                return p;
            }

            p = GameAssembly.FromRva(rva);
            if (p == IntPtr.Zero)
            {
                Diagnostics.Warn($"could not resolve {label}");
                return IntPtr.Zero;
            }

            Diagnostics.Warn($"{label} -> 0x{p.ToInt64():X} (RVA 0x{rva:X}; the game may have been " +
                             "patched since this RVA was read)");
            return p;
        }

        private static IntPtr ViaMethodInfo(string typeName, string methodName) =>
            ViaMethodInfo(typeName, methodName, null);

        private static IntPtr ViaMethodInfo(string typeName, string methodName, string firstParameter)
        {
            try
            {
                if (!InteropTypeIndex.ByName().TryGetValue(typeName, out List<Type> types))
                    return IntPtr.Zero;

                foreach (Type t in types)
                {
                    foreach (MethodInfo mi in t.GetMethods(AnyMethod | BindingFlags.DeclaredOnly))
                    {
                        if (mi.Name != methodName) continue;
                        if (!FirstParameterMatches(mi, firstParameter)) continue;

                        FieldInfo f = Il2CppInterop.Common.Il2CppInteropUtils
                            .GetIl2CppMethodInfoPointerFieldForGeneratedMethod(mi);
                        if (f == null) continue;

                        IntPtr info = (IntPtr)f.GetValue(null);
                        if (info == IntPtr.Zero) continue;

                        // The MethodInfo's first field is the compiled method pointer.
                        IntPtr fn = *(IntPtr*)info;
                        if (fn != IntPtr.Zero) return fn;
                    }
                }
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"MethodInfo lookup for {typeName}.{methodName} failed: " +
                                 Diagnostics.Describe(e));
            }
            return IntPtr.Zero;
        }

        /// <summary>Whether a method's first parameter is of the named type. No name given, no check.</summary>
        private static bool FirstParameterMatches(MethodInfo mi, string firstParameter)
        {
            if (firstParameter == null) return true;

            ParameterInfo[] parameters = mi.GetParameters();
            if (parameters.Length == 0) return false;

            string full = parameters[0].ParameterType.FullName;
            return full != null && full.StartsWith(firstParameter, StringComparison.Ordinal);
        }
    }
}
