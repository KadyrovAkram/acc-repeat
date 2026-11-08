using System;
using System.IO;
using System.Linq;

internal static partial class RepeatCorner
{
    private const int RecentAttemptLimit = 5;

    public sealed class Attempt
    {
        public double Seconds, EntrySpeed, ExitSpeed;
        public double? PreviousBestDelta;
        // Metres driven between the markers; 0 for runs recorded before this was measured.
        public double Path;
    }

    // ACC marks every lap invalid as soon as a save loads, so its lap-validity flag can't
    // separate clean runs from cut ones. A run that is clearly shorter than the usual line
    // through the section is treated as a cut instead.
    private const double CutMinimumMetres = 4, CutFraction = 0.01;

    // The most recent run that was discarded as a cut, until a run is recorded.
    private static Section cutSection;
    private static Attempt cutRun;
    private static double cutShortBy;

    private static void ReportCut(Section section, Attempt attempt, double shortBy)
    {
        cutSection = section;
        cutRun = attempt;
        cutShortBy = shortBy;
        ReportStatus(string.Format("Run not counted: {0:0.00} s, but {1:0} m shorter than your usual line. " +
            "Looks like a cut.", attempt.Seconds, shortBy));
    }

    private static double? CutMetres(Section section, Attempt attempt)
    {
        var paths = section.RecentAttempts.Select(run => run.Path).Where(path => path > 0).OrderBy(path => path).ToArray();
        if (paths.Length == 0 || attempt.Path <= 0) return null;
        double usual = paths[paths.Length / 2];
        double shorter = usual - attempt.Path;
        return shorter > Math.Max(CutMinimumMetres, usual * CutFraction) ? shorter : (double?)null;
    }

    private static void SetFinish(Section section, double position)
    {
        if (section.Finish != position)
        {
            section.RecentAttempts.Clear();
            section.BestAttempt = null;
        }
        section.Finish = position;
    }

    private static double? RecordAttempt(Section section, Attempt attempt)
    {
        var previousBest = section.BestAttempt;
        section.RecentAttempts.Insert(0, attempt);
        if (section.RecentAttempts.Count > RecentAttemptLimit)
            section.RecentAttempts.RemoveRange(RecentAttemptLimit, section.RecentAttempts.Count - RecentAttemptLimit);
        if (previousBest == null || attempt.Seconds < previousBest.Seconds)
            section.BestAttempt = attempt;
        attempt.PreviousBestDelta = previousBest == null ? (double?)null : attempt.Seconds - previousBest.Seconds;
        return attempt.PreviousBestDelta;
    }

    private static string DescribeAttempt(Attempt attempt)
    {
        return string.Format("{0:0.00} s | Entry {1:0} km/h | Exit {2:0} km/h",
            attempt.Seconds, attempt.EntrySpeed, attempt.ExitSpeed);
    }

    private static void ReportAttempt(Section section, Attempt attempt)
    {
        cutSection = null;
        cutRun = null;
        double? delta = RecordAttempt(section, attempt);
        string comparison = delta.HasValue ? string.Format(" | {0:+0.00;-0.00;0.00} s vs previous best", delta.Value) : "";
        bool newBest = !delta.HasValue || delta.Value < 0;
        Console.WriteLine("{0}: {1}{2}{3}", section.Checkpoint ?? section.Track,
            DescribeAttempt(attempt), comparison, newBest ? " | New best" : "");
        if (desktopSession != null) desktopSession.Note(string.Format("Run recorded: {0:0.00} s.", attempt.Seconds));

        try { StoreSettings(); }
        catch (IOException error) { ReportStatus("Could not save attempt history. " + error.Message); }
        catch (UnauthorizedAccessException error) { ReportStatus("Could not save attempt history. " + error.Message); }
    }

    private static void PrintHistory()
    {
        var sections = settings.Sections
            .Where(section => section.Checkpoint == settings.ActiveCheckpoint && section.BestAttempt != null).ToArray();
        if (sections.Length == 0)
            Console.WriteLine("No completed timed runs for this drill yet.");
        foreach (var section in sections)
        {
            Console.WriteLine("{0} | Personal best: {1}", section.Checkpoint ?? section.Track, DescribeAttempt(section.BestAttempt));
            Console.WriteLine("Recent runs (newest first)");
            for (int index = 0; index < section.RecentAttempts.Count; index++)
                Console.WriteLine("  {0}. {1}", index + 1, DescribeAttempt(section.RecentAttempts[index]));
        }
    }
}
