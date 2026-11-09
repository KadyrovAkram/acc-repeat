using System;
using System.IO;
using System.Reflection;

// Writes ACCRepeat.log beside the executable so a user who reports a failed reset can
// attach a record of each reload step, the menus it recognized, and where it stopped.
// Logging must never break the tool, so every write swallows I/O errors.
internal static class DiagnosticLog
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new object();
    private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ACCRepeat.log");
    private static readonly string PreviousLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ACCRepeat.previous.log");

    public static void Info(string message) { Write("INFO ", message); }

    public static void Warn(string message) { Write("WARN ", message); }

    public static void Error(string message, Exception error)
    {
        Write("ERROR", message + " [" + error.GetType().Name + "] " + error.Message);
    }

    public static void Started()
    {
        Info(string.Format("ACC Repeat {0} started on {1}, .NET {2}.",
            Assembly.GetExecutingAssembly().GetName().Version, Environment.OSVersion, Environment.Version));
    }

    private static void Write(string level, string message)
    {
        string line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} {1} {2}{3}", DateTime.Now, level, message, Environment.NewLine);
        lock (Gate)
        {
            try
            {
                RotateIfLarge();
                File.AppendAllText(LogPath, line);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // Keep one previous file so the log can't grow without bound but the run before the
    // current one is still there when someone reports a problem.
    private static void RotateIfLarge()
    {
        var file = new FileInfo(LogPath);
        if (!file.Exists || file.Length < MaxBytes) return;
        if (File.Exists(PreviousLogPath)) File.Delete(PreviousLogPath);
        File.Move(LogPath, PreviousLogPath);
    }
}
