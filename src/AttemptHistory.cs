using System;
using System.IO;
using System.Linq;

internal static partial class RepeatCorner
{
    private const int RecentAttemptLimit = 5;

    public sealed class Attempt
    {
        public double Seconds, EntrySpeed, ExitSpeed;
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
        return previousBest == null ? (double?)null : attempt.Seconds - previousBest.Seconds;
    }

    private static string DescribeAttempt(Attempt attempt)
    {
        return string.Format("{0:0.00} s | Entry {1:0} km/h | Exit {2:0} km/h",
            attempt.Seconds, attempt.EntrySpeed, attempt.ExitSpeed);
    }

    private static void ReportAttempt(Section section, Attempt attempt)
    {
        double? delta = RecordAttempt(section, attempt);
        string comparison = delta.HasValue ? string.Format(" | {0:+0.00;-0.00;0.00} s vs previous best", delta.Value) : "";
        bool newBest = !delta.HasValue || delta.Value < 0;
        Console.WriteLine("{0}: {1}{2}{3}", section.Checkpoint ?? section.Track,
            DescribeAttempt(attempt), comparison, newBest ? " | New best" : "");

        try { StoreSettings(); }
        catch (IOException error) { Console.WriteLine("Could not save attempt history: " + error.Message); }
        catch (UnauthorizedAccessException error) { Console.WriteLine("Could not save attempt history: " + error.Message); }
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
