using System.Diagnostics;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// Logging, under one prefix so that everything this mod says can be found in a game log that
    /// also carries MelonLoader's and every other mod's output.
    ///
    /// Debug builds only. `[Conditional("DEBUG")]` removes the call — and with it the string its
    /// argument builds — from a Release build, so a Release assembly carries no log text at all.
    /// Nothing needs an `#if` at the call site: the reason for writing a line is a debugging one, so
    /// the mechanism is in one place rather than at a hundred.
    ///
    /// What that costs: a Release build is silent. That is the trade the mod is built on — the
    /// function is what ships, and if something needs explaining, `build.bat Debug` is the build
    /// that explains it.
    ///
    /// One consequence worth knowing, because it is easy to get backwards: `[Conditional]` removes
    /// the call and the argument's evaluation, but the argument still has to compile. So a method
    /// named inside a log argument (`Describe`, `Summary`) must exist in Release as well, however
    /// little of it is left.
    /// </summary>
    internal static class Diagnostics
    {
        [Conditional("DEBUG")]
        internal static void Info(string message)
        {
#if DEBUG
            MelonLoader.MelonLogger.Msg("[AutoPlay] " + message);
#endif
        }

        [Conditional("DEBUG")]
        internal static void Warn(string message)
        {
#if DEBUG
            MelonLoader.MelonLogger.Warning("[AutoPlay] " + message);
#endif
        }

        [Conditional("DEBUG")]
        internal static void Error(string message)
        {
#if DEBUG
            MelonLoader.MelonLogger.Error("[AutoPlay] " + message);
#endif
        }

        /// <summary>
        /// Flattens an exception chain into one line, outermost cause first.
        ///
        /// This is the one thing every failure in this mod needs: reflection and interop wrap the
        /// real cause in an InnerException, so the top-level message on its own rarely says what is
        /// wrong.
        ///
        /// The walk is Debug-only; Release keeps the type name, because the method has to exist for
        /// the log calls that name it (see the class summary) and those calls are gone anyway.
        /// </summary>
        internal static string Describe(System.Exception e)
        {
            if (e == null) return "<null>";

#if DEBUG
            var sb = new System.Text.StringBuilder();
            for (System.Exception x = e; x != null; x = x.InnerException)
            {
                if (sb.Length > 0) sb.Append(" <- ");
                sb.Append(x.GetType().Name).Append(": ").Append(x.Message);
            }
            return sb.ToString();
#else
            return e.GetType().Name;
#endif
        }
    }
}
