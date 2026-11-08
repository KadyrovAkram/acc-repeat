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

    // The first runs on new markers have nothing to compare against, so a cut can be
    // recorded, even as the personal best. Once three runs have a measured distance, the
    // same check is applied to every recorded run and the cuts are dropped.
    private static int PruneCuts(Section section)
    {
        var paths = section.RecentAttempts.Select(run => run.Path).Where(path => path > 0).OrderBy(path => path).ToArray();
        if (paths.Length < 3) return 0;
        double usual = paths[paths.Length / 2];
        Func<Attempt, bool> isCut = run => run.Path > 0 && usual - run.Path > Math.Max(CutMinimumMetres, usual * CutFraction);
        var cuts = section.RecentAttempts.Where(isCut).ToList();
        bool bestIsCut = section.BestAttempt != null && isCut(section.BestAttempt);
        int removed = cuts.Count + (bestIsCut && !cuts.Any(run => SameRun(run, section.BestAttempt)) ? 1 : 0);
        section.RecentAttempts.RemoveAll(run => cuts.Contains(run));
        if (bestIsCut) section.BestAttempt = QuickestRecent(section);
        return removed;
    }

    private static void PruneAllCuts()
    {
        int removed = settings.Sections.Sum(section => PruneCuts(section));
        if (removed == 0) return;
        StoreSettings();
        ReportStatus(string.Format("Removed {0} earlier run{1} that look like cuts.", removed, removed == 1 ? "" : "s"));
    }

    private static bool SameRun(Attempt left, Attempt right)
    {
        return left == right || (left.Seconds == right.Seconds && left.EntrySpeed == right.EntrySpeed &&
            left.ExitSpeed == right.ExitSpeed && left.Path == right.Path);
    }

    private static Attempt QuickestRecent(Section section)
    {
        return section.RecentAttempts.OrderBy(run => run.Seconds).FirstOrDefault();
    }

    // Removes one run the driver doesn't want counted. A removed personal best is replaced by
    // the quickest run left in the recent list; an older best that already dropped out is gone.
    private static void RemoveAttempt(Section section, Attempt run)
    {
        bool wasBest = DropAttempt(section, run);
        StoreSettings();
        ReportStatus(string.Format("Removed the {0:0.000} s run.{1}", run.Seconds, !wasBest ? "" :
            section.BestAttempt == null ? " No personal best left." :
            string.Format(" Your best is now {0:0.000} s.", section.BestAttempt.Seconds)));
    }

    // Returns whether the removed run was the personal best.
    private static bool DropAttempt(Section section, Attempt run)
    {
        bool wasBest = section.BestAttempt != null && SameRun(section.BestAttempt, run);
        int index = section.RecentAttempts.FindIndex(recent => SameRun(recent, run));
        if (index < 0 && !wasBest)
            throw new InvalidOperationException("That run is no longer in the history.");
        if (index >= 0) section.RecentAttempts.RemoveAt(index);
        if (wasBest) section.BestAttempt = QuickestRecent(section);
        return wasBest;
    }

    // Desk: the run object comes from the snapshot, which shares the engine's instances.
    private static void RemoveRun(Attempt run)
    {
        var section = settings.Sections.Find(candidate => candidate.Checkpoint == settings.ActiveCheckpoint &&
            (candidate.RecentAttempts.Contains(run) || candidate.BestAttempt == run));
        if (section == null)
            throw new InvalidOperationException("That run is no longer in the history.");
        RemoveAttempt(section, run);
    }

    // Console: 'history remove <1-5>' or 'history remove best' for the active drill.
    private static void ConfigureHistory(string arguments)
    {
        var parts = arguments.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            PrintHistory();
            return;
        }
        int number = 0;
        bool best = parts.Length == 2 && parts[1].Equals("best", StringComparison.OrdinalIgnoreCase);
        if (parts.Length != 2 || !parts[0].Equals("remove", StringComparison.OrdinalIgnoreCase) ||
            (!best && (!int.TryParse(parts[1], out number) || number < 1 || number > RecentAttemptLimit)))
            throw new InvalidOperationException("Use 'history', 'history remove <1-5>' or 'history remove best'.");
        var sections = settings.Sections.Where(section => section.Checkpoint == settings.ActiveCheckpoint &&
            (section.RecentAttempts.Count != 0 || section.BestAttempt != null)).ToArray();
        if (sections.Length != 1)
            throw new InvalidOperationException(sections.Length == 0 ? "There are no runs to remove." :
                "This drill has runs on more than one track. Pin a checkpoint for the track first.");
        var target = sections[0];
        if (best && target.BestAttempt == null) throw new InvalidOperationException("There is no personal best.");
        if (!best && number > target.RecentAttempts.Count) throw new InvalidOperationException("There is no run " + number + ".");
        RemoveAttempt(target, best ? target.BestAttempt : target.RecentAttempts[number - 1]);
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
        int pruned = PruneCuts(section);
        string note = string.Format("Run recorded: {0:0.00} s.", attempt.Seconds);
        if (pruned != 0)
        {
            note += string.Format(" Removed {0} earlier run{1} that look like cuts.", pruned, pruned == 1 ? "" : "s");
            Console.WriteLine(note);
        }
        if (desktopSession != null) desktopSession.Note(note);

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
