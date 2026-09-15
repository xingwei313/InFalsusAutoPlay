using System;
using System.Runtime.InteropServices;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// Ends a song the mod played by going back to the song list instead of on to the results screen,
    /// so that the play leaves no record.
    ///
    /// <para>
    /// The results screen is the only thing in the game that writes one. `ResultsScene.Start` calls
    /// `GameResultsV4.TryUpdate` for the song's best score and `EncounterResults.UpdateEncounterResult`
    /// for the encounter's, and neither call has a caller anywhere else in the binary — so a play that
    /// never reaches that scene leaves nothing behind: no score, no clear, no unlock, no loot. Nothing
    /// here edits a score or a verdict; the code that writes the record simply never runs.
    /// </para>
    /// <para>
    /// The decision is taken at the scene transition rather than inside the results screen, and that is
    /// not a preference. The write happens in the scene's own `Start`, so a results scene whose `Start`
    /// was skipped is a scene that never reports itself ready — and the transition that is waiting for
    /// it waits forever.
    /// </para>
    /// <para>
    /// What is asked for instead is the transition the game itself performs after a song it wants no
    /// result for. Every gameplay mode that ends without one calls `GameScene._Ok()` and then
    /// `CoreScene._oH&lt;GameScene, SongSelectScene&gt;(2, null, null, null, null)`. `_oH` reduces to a
    /// scene transition by name — `typeof(TUnloadScene).Name` and `typeof(TLoadScene).Name` — so the
    /// redirection is exactly that call, reached by rewriting the destination of the transition the
    /// game was already making: same flags, same scene being left, same callbacks dropped, one name
    /// changed.
    /// </para>
    /// <para>
    /// Both names are checked rather than only the destination: a results screen reached from anywhere
    /// other than the gameplay scene would be answered with the wrong scene rather than with a skip,
    /// and only the end of a play is what this is for.
    /// </para>
    /// </summary>
    internal static class ResultsSkip
    {
        /// <summary>The scene a play ends in, the one being skipped, and the one to go to instead.</summary>
        private const string GameplayScene = "GameScene";
        private const string ResultsScene = "ResultsScene";
        private const string SongListScene = "SongSelectScene";

        /// <summary>
        /// `GameScene._Ok()` — `void(argument)`. The gameplay scene putting its session state down;
        /// every mode that ends without results calls it, and so does the results screen's own
        /// loaded-callback.
        ///
        /// `_Ok`, not `_ok`: the game has both, one letter apart, taking different arguments —
        /// `_ok(_nG)` is the sibling that is handed the gameplay history.
        /// </summary>
        private const long RvaLeaveGameplay = 0x6F2220;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void LeaveGameplayFn(IntPtr argument);

        private static LeaveGameplayFn _leave;
        private static bool _resolved;
        private static int _failures;

        private static long _skips;

        /// <summary>How many plays have been sent to the song list. For the status line.</summary>
        internal static long Skips => _skips;

        /// <summary>
        /// Whether the mod should take the record away from the play that is ending.
        ///
        /// `Hooks.Writing` rather than `Config.Autoplay` on its own, so that this follows the same
        /// notion of "the mod is playing this" as everything else in the mod: a run whose switch was
        /// turned off mid-song, or in which a detour has faulted, is a run the player finished — or
        /// took over — themselves.
        /// </summary>
        private static bool ShouldSkip => Config.NoScore && Hooks.Writing;

        /// <summary>
        /// Whether this transition is the end of an autoplayed song being sent to the results screen —
        /// and, if it is, the scene name to send it to instead.
        ///
        /// Called from the `CoreScene._bj` detour before the original runs, so nothing has been done
        /// yet: the transition has not been started, and no scene has been loaded and thrown away. The
        /// names are the strings the game itself built for this transition, which is why nothing here
        /// has to know how a scene is addressed.
        /// </summary>
        internal static bool Redirects(IntPtr unloadName, IntPtr loadName, out IntPtr destination)
        {
            destination = IntPtr.Zero;
            if (!ShouldSkip) return false;

            string from = Memory.Text(unloadName);
            string to = Memory.Text(loadName);

            // Every transition the mod looks at is reported with the names the game itself built for
            // it. That line is the evidence for this feature: a name read wrongly shows up as the
            // question marks rather than as a skip that quietly stopped happening. Debug-only, like
            // every other line in the mod — the call and its argument are gone from a Release build.
            Diagnostics.Info($"scene transition: {from ?? "?"} -> {to ?? "?"}");

            if (from != GameplayScene || to != ResultsScene) return false;

            // The results screen's loaded-callback is what would have run this, once the scene it loads
            // was up. That callback is the one being dropped, so its work is done here instead — and in
            // the order the game's own no-results modes do it, before the transition rather than after.
            LeaveGameplay();

            // A fresh string every time, never one kept to be reused: an IL2CPP object's address held in
            // a static field on this side is invisible to the IL2CPP collector, which looks at its own
            // heap and its own roots. Between here and the game storing the pointer in the transition's
            // state object it is in a register or on the stack — which that collector does scan — so it
            // is safe for exactly as long as it needs to be.
            destination = Il2CppInterop.Runtime.IL2CPP.il2cpp_string_new(SongListScene);
            if (destination == IntPtr.Zero)
            {
                Diagnostics.Warn("could not build the song list's scene name, so the results screen " +
                                 "will be shown and this play will be recorded");
                return false;
            }

            _skips++;
            Diagnostics.Info($"results skipped: {from} -> {SongListScene} instead of {to}, " +
                             "so this play is not recorded");
            return true;
        }

        /// <summary>Reports a fault without switching autoplay off — a scene change is expected here.</summary>
        internal static void NoteFailure(Exception e)
        {
            _failures++;
            if (_failures <= 3)
                Diagnostics.Warn($"a scene transition could not be redirected ({_failures}): " +
                                 Diagnostics.Describe(e));
        }

        /// <summary>
        /// `GameScene._Ok()`, resolved the first time one is needed rather than at startup.
        ///
        /// It is a method of the scene being left, and by the time a song has ended the game has long
        /// since initialised it — so the lookup finds the runtime MethodInfo, which survives a game
        /// update, instead of falling back to the address in dump.cs. (<see cref="Tooltip"/> resolves
        /// its object lazily on the same reasoning.)
        /// </summary>
        private static void LeaveGameplay()
        {
            if (!_resolved)
            {
                _resolved = true;
                IntPtr target = MethodResolver.ByName(GameplayScene, "_Ok", RvaLeaveGameplay);
                if (target != IntPtr.Zero)
                    _leave = Marshal.GetDelegateForFunctionPointer<LeaveGameplayFn>(target);
            }

            if (_leave == null) return;

            // The argument is zero because that is what the game passes: both of the binary's call
            // sites zero the register first. `_Ok` is a forwarder that does not read it, so leaving
            // anything else there would be a difference for no reason.
            _leave(IntPtr.Zero);
        }
    }
}
