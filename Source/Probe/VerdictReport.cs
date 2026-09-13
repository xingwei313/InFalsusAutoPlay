using System.Text;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// How a tally is printed — debug builds only. The tally itself is in
    /// <see cref="Verdicts"/>, which is the part that runs in Release.
    /// </summary>
    internal static class VerdictReport
    {
        /// <summary>
        /// One note's verdict, in the game's own terms rather than the mod's — this is what
        /// `PerNoteLog` buys, and a run that is being diagnosed reads it line by line:
        ///
        ///     result sky[307] t=99841..100127 type=5 side=4 lanes=-1..-1 x=0.143->0.143 grade=Perfect judged=1
        /// </summary>
        internal static void Note(Verdicts tally, Chart chart, int index, ChartNote n,
                                  byte grade, byte judged)
        {
            string kind = n.IsSky ? $"sky[{SkyOrdinal(chart, index)}]" : "floor";

            Diagnostics.Info($"result {kind} t={n.StartMs}..{n.EndMs} type={n.Type} side={n.Side} " +
                             $"lanes={n.LaneFirst}..{n.LaneLast} x={n.StartX:F3}->{n.EndX:F3} " +
                             $"grade={(grade == Grade.Unreadable ? "unreadable" : Report.GradeName(grade))} " +
                             $"judged={judged}");
        }

        /// <summary>
        /// The distribution, printed when the song finishes and, at <see cref="Config.ReportSeconds"/>
        /// intervals, whenever something is wrong with it. A run that is going well says nothing until
        /// the end.
        /// </summary>
        internal static void Final(Verdicts tally, Chart chart) => Print(tally, chart, final: true);

        internal static void Interim(Verdicts tally, Chart chart) => Print(tally, chart, final: false);

        private static void Print(Verdicts tally, Chart chart, bool final)
        {
            var sb = new StringBuilder();
            sb.Append(final ? "verdicts FINAL: " : "verdicts: ");
            sb.Append($"{tally.Tallied}/{chart.Notes.Length} tallied");

            sb.Append(" |");
            for (int g = 0; g <= Grade.Perfect; g++)
                if (tally.ByGrade((byte)g) > 0)
                    sb.Append($" {Report.GradeName((byte)g)}={tally.ByGrade((byte)g)}");

            sb.Append($" | unjudged={tally.Unjudged} unreadable={tally.Unreadable}");

            // The one diagnosis worth spelling out: its symptom reads like a timing problem, so it is
            // named here rather than left to be inferred from the histogram.
            if (tally.ByGrade(Grade.None) > 0 || tally.Unjudged > 0)
                sb.Append("  <- a sky bar's tick was skipped; check that the _VD._oz hook installed");

            Diagnostics.Info(sb.ToString());
        }

        /// <summary>
        /// Notes[i] is the ordinal-th sky note, or -1 for a floor note. Built on demand and kept until
        /// the chart is republished — it is only ever wanted for a log line, so a Release build neither
        /// computes nor carries it.
        /// </summary>
        private static int SkyOrdinal(Chart chart, int index)
        {
            if (chart.Revision != _ordinalRevision)
            {
                _ordinalRevision = chart.Revision;
                _ordinals = new int[chart.Notes.Length];

                int sky = 0;
                for (int i = 0; i < chart.Notes.Length; i++)
                    _ordinals[i] = chart.Notes[i].IsSky ? sky++ : -1;
            }

            return index >= 0 && index < _ordinals.Length ? _ordinals[index] : -1;
        }

        private static int[] _ordinals = System.Array.Empty<int>();
        private static int _ordinalRevision = -1;
    }
}
