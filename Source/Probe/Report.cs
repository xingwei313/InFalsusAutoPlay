namespace InFalsusAutoPlay
{
    /// <summary>
    /// The one line that says what every part of the mod has been doing — debug builds only.
    ///
    /// It reads the live objects rather than a copy of their counters — the probe is in the same
    /// assembly, so there is no second surface to keep in step with the state it describes.
    /// </summary>
    internal static class Report
    {
        internal static string Line()
        {
            Song song = Song.Current;

            return $"chart: {song.Chart.Status} | " +
                   $"lanes: t={(song.NowMs < 0 ? "n/a" : $"{song.NowMs:F0}ms")} {Lanes(song)} | " +
                   $"sky: skyPins={song.Bar.Pins} aims={song.Sky.Aims} " +
                   $"cursor={(song.Sky.LastAim < 0f ? "n/a" : song.Sky.LastAim.ToString("F3"))} | " +
                   $"{Verdicts(song)} | " +
                   $"judgementPoint={JudgementPoint.Suppressed} ghostRescues={JudgementPoint.Rescues} | " +
                   $"{Row()} | " +
                   $"resultsSkipped={ResultsSkip.Skips} | " +
                   $"hooks: skyJudgements={Hooks.SkyJudgements} trackEnables={Hooks.TrackEnables}";
        }

        /// <summary>
        /// Includes the song clock and the next note it expects, so a chart read that does not line up
        /// with the song shows as a number rather than as silence.
        /// </summary>
        private static string Lanes(Song song)
        {
            ChartNote[] notes = song.Chart.Notes;
            int cursor = song.Floor.Cursor;
            string next = cursor < notes.Length ? $"{notes[cursor].StartMs}ms" : "end";

            return $"next={next} ticks={song.Floor.Ticks} presses={song.Floor.Presses} " +
                   $"late={song.Floor.Late}";
        }

        private static string Verdicts(Song song)
        {
            int total = song.Chart.Notes.Length;
            return total == 0
                ? "verdicts=n/a"
                : $"verdicts={song.Verdicts.Tallied}/{total} " +
                  $"bad={song.Verdicts.Unjudged + song.Verdicts.Unreadable}";
        }

        private static string Row()
        {
            AssistRow row = AssistRow.Current;
            if (row != null)
                return $"autoRow=taken on={(Config.Autoplay ? 1 : 0)} " +
                       $"syncs={row.Syncs} presses={row.Presses}";

            return Config.SettingsRow ? "autoRow=idle" : "autoRow=off";
        }

        /// <summary>
        /// Occasional one-line evidence that the schedule is where it should be, printed only when it
        /// changes and only while dry-running. A still line means a stalled cursor, which is the failure
        /// this is here to make visible.
        /// </summary>
        internal static void DryRunTrace()
        {
            Song song = Song.Current;
            string line = $"t={song.NowMs:F0}ms next={song.Floor.Cursor}/{song.Chart.Notes.Length} " +
                          $"hold=[{song.Floor.ScheduleText()}]";

            if (line == _lastTrace) return;
            _lastTrace = line;
            Diagnostics.Info(line);
        }

        private static string _lastTrace = "";

        /// <summary>Human-readable grade name, for the log lines.</summary>
        internal static string GradeName(byte g) => g switch
        {
            Grade.None => "None",
            Grade.Miss => "Miss",
            Grade.FarEarly => "FarEarly",
            Grade.FarLate => "FarLate",
            Grade.NearEarly => "NearEarly",
            Grade.NearLate => "NearLate",
            Grade.Perfect => "Perfect",
            _ => $"#{g}",
        };
    }
}
