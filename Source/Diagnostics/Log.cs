using System.Diagnostics;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// Logging. Every line goes out through MelonLoader's logger, which prefixes it with this mod's
    /// assembly name — so this mod's lines are already findable in a log that also carries
    /// MelonLoader's and every other mod's output, and adding a tag of its own would print the mod's
    /// name twice.
    ///
    /// <para>
    /// Four levels, and which of them a Release build carries is the whole of the design:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="Load"/> is compiled into every build and is deliberately three lines: loaded, the
    /// offset summary, the installed-hook count. A release is the build users run, so it has to be
    /// able to say whether the mod is working and whether a game update moved something — but the
    /// signal for the second is `moved=3`, and which three is the debug build's job.
    /// </description></item>
    /// <item><description>
    /// <see cref="Error"/> is compiled into every build. It is the one thing a release must never
    /// swallow: the mod has stopped doing its work, and nobody is going to look for that in a build
    /// they were told to reinstall.
    /// </description></item>
    /// <item><description>
    /// <see cref="Info"/> and <see cref="Warn"/> are Debug-only, and by a compile-time switch rather
    /// than a runtime one: `[Conditional("DEBUG")]` removes the call <b>and the string its argument
    /// builds</b>, so a Release assembly carries neither the narration about what the mod is doing
    /// while playing nor the per-item detail of its own loading — no per-frame status, no chart
    /// contents, no settings-page walkthrough, no probes, no per-hook and per-offset lines. That is
    /// the bulk of what this class emits, and keeping it out is what makes a release small.
    /// </description></item>
    /// </list>
    /// <para>
    /// The config path is deliberately not in any of them: `config: &lt;path&gt;` is an
    /// <see cref="Info"/> line, so a release does not print where the user's file is.
    /// </para>
    /// <para>
    /// One consequence worth knowing, because it is easy to get backwards: `[Conditional]` removes the
    /// call and the argument's evaluation, but the argument still has to compile. So a method named
    /// inside a log argument (`Describe`, `Summary`) must exist in Release as well, however little of
    /// it is left.
    /// </para>
    /// </summary>
    internal static class Diagnostics
    {
        /// <summary>Narration about a run — Debug builds only. See the class summary.</summary>
        [Conditional("DEBUG")]
        internal static void Info(string message)
        {
#if DEBUG
            MelonLoader.MelonLogger.Msg(message);
#endif
        }

        /// <summary>
        /// The three lines that say the mod is here and healthy, in every build: that it loaded, what
        /// the game answered when asked where its fields are, and how many hooks went in.
        ///
        /// Deliberately few. A release is the build users run and it has to be able to answer "is this
        /// working, and did a game update move something" without a debug build — but the answer to
        /// both is a summary: `moved=3` is the signal, and which three is what the debug build is for.
        /// Every line here is a string in the shipped assembly, and a release that carries a
        /// per-offset, per-method, per-hook narration of its own loading is a release paying for a log
        /// nobody reads.
        /// </summary>
        internal static void Load(string message) => MelonLoader.MelonLogger.Msg(message);

        /// <summary>Something is wrong and the mod has stopped doing it: a detour faulted.</summary>
        internal static void Error(string message) => MelonLoader.MelonLogger.Error(message);

        /// <summary>
        /// Something is wrong and the mod works around it: a missing hook, an offset that would not
        /// resolve. Debug builds only, like <see cref="Info"/> — see the class summary — and the
        /// summary lines above are what a release has instead.
        /// </summary>
        [Conditional("DEBUG")]
        internal static void Warn(string message)
        {
#if DEBUG
            MelonLoader.MelonLogger.Warning(message);
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
