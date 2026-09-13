using System;
using System.Collections.Generic;
using System.Reflection;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// The generated interop types, indexed by name.
    ///
    /// Types are looked up by name at runtime rather than with compile-time `typeof()`: the interop
    /// namespaces are generated (`Il2Cpp` + the obfuscated namespace) and a wrong guess would be a
    /// build error instead of a fallback.
    ///
    /// Walking the `Il2Cpp*` assemblies is slow — tens of thousands of generated types — and every
    /// method resolution wants the same walk. Indexing them by name once turns each lookup into a
    /// dictionary hit. The index is built on first use, which is during hook installation.
    /// </summary>
    internal static class InteropTypeIndex
    {
        private static Dictionary<string, List<Type>> _byName;

        /// <summary>Generated types by simple name. A name can hit more than one type.</summary>
        internal static Dictionary<string, List<Type>> ByName()
        {
            if (_byName != null) return _byName;

            var index = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
            int scanned = 0;

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = asm.GetName().Name;
                if (name == null || !name.StartsWith("Il2Cpp", StringComparison.Ordinal)) continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }

                foreach (Type t in types)
                {
                    if (t == null) continue;
                    scanned++;

                    if (!index.TryGetValue(t.Name, out List<Type> bucket))
                        index[t.Name] = bucket = new List<Type>(1);
                    bucket.Add(t);
                }
            }

            Diagnostics.Info($"indexed {scanned} interop types by name");
            return _byName = index;
        }
    }
}
