using System;
using System.Collections.Generic;

namespace InFalsusAutoPlay
{
    /// <summary>
    /// Reads back what the game decided for every note, and reports the distribution.
    ///
    /// This is the mod's evidence, and it is deliberately not a restatement of anything the mod
    /// believes: the grade comes out of `_cH._wdb` in the judgement-group dictionary at `_T._XC`,
    /// which is the same byte `_VD._Oz` and `_VD._nz` grade into. Nothing here can disagree with the
    /// game, because nothing here decides anything.
    ///
    /// It exists because the alternative — watching the screen and concluding "that looked perfect" —
    /// is not evidence. A distribution over every note answers "did the run grade correctly" in one
    /// line, and it answers it for the floor lanes too, where "it works" is not a measurement.
    ///
    /// How to read a report:
    ///
    ///     verdicts FINAL: 818/818 tallied | Perfect=812 NearLate=6 | unjudged=0 unreadable=0
    ///
    /// <list type="bullet">
    /// <item><description>`Perfect` for every note is the intended result.</description></item>
    /// <item><description>`None` or `unjudged` is the failure signature for a sky bar: `_Ae._tfA`
    /// was 0 at the check, so `_VD._Oz` took its `break` and skipped the bar rather than grading it.
    /// In practice that means the `_VD._oz` hook is not firing.</description></item>
    /// <item><description>`Miss` with `unjudged=0` is the other sky-bar failure: the bar was graded,
    /// but `_Ae._pfA` was above 0 when it was read — again the `_VD._oz` hook.</description></item>
    /// <item><description>A floor note that is not Perfect is a timing fault, and the `x`/`t` in
    /// the per-note line says which note.</description></item>
    /// </list>
    ///
    /// A note is counted at most once, and what makes that true is the note id, not a position. The
    /// chart is re-read a few times early in a song (a sky plane can be absent on the frame the scene
    /// loads), and a re-read that adds notes inserts them throughout the ordering — so an index that
    /// was correct for the old array can point at a different note in the new one. Counting by
    /// position would tally some notes twice and miss others, which would corrupt exactly the
    /// histogram this class exists to produce.
    /// </summary>
    internal sealed class Verdicts
    {
        /// <summary>
        /// How long past a note's end to wait before asking for its grade. A bar's last tick is
        /// graded on the frame the bar ends, so reading on that exact frame can catch the group
        /// before it has been written.
        /// </summary>
        private const double SettleMs = 80.0;

        private readonly Chart _chart;

        /// <summary>How many notes landed on each grade; indexed by the grade byte.</summary>
        private readonly int[] _byGrade = new int[8];

        /// <summary>Every note already counted. This is what makes the tally idempotent.</summary>
        private readonly HashSet<long> _counted = new HashSet<long>();

        private int _cursor;          // fast path into Chart.VerdictOrder
        private int _revision = -1;
        private int _lastLength = -1;
        private double _nextReportMs;
        private bool _complete;

        // Read by the status line and the report.
        internal int Tallied;
        internal int Unreadable;      // no group found for the note id
        internal int Unjudged;        // group found, but the judged flag is clear
        internal int ByGrade(byte g) => _byGrade[g & 7];

        internal Verdicts(Chart chart)
        {
            _chart = chart;
        }

        /// <summary>
        /// Called once per gameplay tick, whether or not the mod is writing anything — in observe
        /// mode this is the whole point.
        /// </summary>
        internal void Tick(double nowMs)
        {
            if (!_chart.Loaded) return;

            if (_chart.Revision != _revision) Resync(nowMs);

            int[] order = _chart.VerdictOrder;
            while (_cursor < order.Length && Finished(order[_cursor], nowMs))
                TallyAt(_cursor++);

            if (_cursor >= order.Length && !_complete)
            {
                _complete = true;
#if DEBUG
                VerdictReport.Final(this, _chart);
#endif
                return;
            }

            if (nowMs >= _nextReportMs)
            {
                _nextReportMs = nowMs + Config.ReportSeconds * 1000.0;

                // Under dryrun nothing was written, so a periodic report would only ever say
                // "everything is unjudged". The schedule trace is the useful signal there.
#if DEBUG
                if (!Config.DryRun &&
                    (Unjudged > 0 || Unreadable > 0 || _byGrade[Grade.Miss] > 0))
                    VerdictReport.Interim(this, _chart);
#endif
            }
        }

        private bool Finished(int index, double nowMs) =>
            _chart.Notes[index].EndMs + SettleMs <= nowMs;

        /// <summary>
        /// Reads one note's grade out of the game and adds it to the tally. Idempotent: a note already
        /// counted is ignored, so it is safe to call over a range that may include notes handled
        /// before a re-read.
        /// </summary>
        private void TallyAt(int position)
        {
            int index = _chart.VerdictOrder[position];
            ChartNote n = _chart.Notes[index];

            if (!_counted.Add(n.NoteId)) return;

            if (!_chart.TryReadGroup(n.NoteId, out byte grade, out byte judged))
                grade = Grade.Unreadable;

            Tallied++;

            // A note whose group could not be found is counted once, as unreadable. Counting it as
            // unjudged as well would report every read failure twice and, worse, would raise the
            // sky-bar warning for a reason that has nothing to do with it.
            if (grade == Grade.Unreadable)
            {
                Unreadable++;
            }
            else
            {
                _byGrade[grade & 7]++;
                if (judged == 0) Unjudged++;
            }

#if DEBUG
            if (Config.PerNoteLog)
                VerdictReport.Note(this, _chart, index, n, grade, judged);
#endif
        }

        /// <summary>
        /// The chart was (re)published, so the arrays the cursor indexes have been replaced.
        ///
        /// Every finished note in the new ordering is offered to <see cref="TallyAt"/>, which ignores
        /// the ones already counted. That is deliberately a walk over notes rather than a repositioned
        /// cursor: a re-read can insert notes anywhere, so the only safe question is "has this note
        /// been counted", which the note id answers.
        /// </summary>
        private void Resync(double nowMs)
        {
            _revision = _chart.Revision;

            int[] order = _chart.VerdictOrder;

            // A re-read that adds notes makes an already-printed FINAL report premature, so it is
            // re-armed. One that changes nothing does not — re-printing an identical line is just
            // noise in a log that is meant to be read.
            if (order.Length != _lastLength) _complete = false;
            _lastLength = order.Length;

            // The new chart holds fewer notes than have been tallied: the two disagree, and a count
            // larger than the chart it is reported against would be worse than no count.
            if (Tallied > order.Length)
            {
                Diagnostics.Warn($"chart now has {order.Length} notes but {Tallied} were already " +
                                 "tallied; restarting the tally");
                Clear();
            }

            int i = 0;
            while (i < order.Length && Finished(order[i], nowMs)) TallyAt(i++);
            _cursor = i;
        }

        private void Clear()
        {
            Tallied = 0;
            Unreadable = 0;
            Unjudged = 0;
            // A restarted tally has not reported yet, so the FINAL line is re-armed here rather than
            // at each call site. Both callers want it: a new song, and the shrink path.
            _complete = false;
            _counted.Clear();
            Array.Clear(_byGrade, 0, _byGrade.Length);
        }
    }
}
