using System;
using System.Collections.Concurrent;
using System.Threading;

internal static partial class RepeatCorner
{
    private static readonly ConcurrentQueue<string> ConsoleCommands = new ConcurrentQueue<string>();

    private static void StartConsoleInput()
    {
        var thread = new Thread(() =>
        {
            string line;
            while ((line = Console.ReadLine()) != null)
                ConsoleCommands.Enqueue(line);
        });
        thread.IsBackground = true;
        thread.Start();
    }

    private static bool ProcessConsoleInput()
    {
        string line;
        while (ConsoleCommands.TryDequeue(out line))
        {
            string[] parts = line.Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            string args = parts.Length == 2 ? parts[1].Trim() : "";
            if (parts[0].Equals("quit", StringComparison.OrdinalIgnoreCase)) return false;
            ApplyCommand(() =>
            {
                switch (parts[0].ToLowerInvariant())
                {
                    case "saves": PrintSaves(); break;
                    case "pin": PinSave(args); break;
                    case "use": UseCheckpoint(args); break;
                    case "latest": UseLatest(); break;
                    case "pins": PrintCheckpoints(); break;
                    case "wheel": ConfigureWheel(args); break;
                    case "section": PrintSections(); break;
                    case "history": ConfigureHistory(args); break;
                    case "loop": ConfigureLoop(args); break;
                    case "help": PrintHelp(); break;
                    default: Console.WriteLine("Unknown command. Type 'help' for commands."); break;
                }
            });
        }
        return true;
    }

    private static void ApplyCommand(Action command)
    {
        string original = Json.Serialize(settings);
        try { command(); }
        catch (Exception error)
        {
            settings = Json.Deserialize<ToolSettings>(original);
            ReportStatus(error.Message);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("saves                  List the ACC saves in menu order");
        Console.WriteLine("pin <number> <name>    Pin or rename a save and make it active");
        Console.WriteLine("pins                   List pins and show the active save");
        Console.WriteLine("use <name>             Activate a pinned save");
        Console.WriteLine("latest                 Switch back to the newest save");
        Console.WriteLine("wheel bind             Learn the next wheel or controller button you press");
        Console.WriteLine("wheel clear            Remove the wheel binding");
        Console.WriteLine("wheel                  Show the wheel binding");
        Console.WriteLine("section                Show the timing lines for the active section");
        Console.WriteLine("history                Show the personal best and last five runs");
        Console.WriteLine("history remove <1-5>   Remove a run you don't want counted (numbers from 'history')");
        Console.WriteLine("history remove best    Remove the personal best");
        Console.WriteLine("loop on [seconds]      Enable auto-reset after a timed finish (default 3 seconds)");
        Console.WriteLine("loop off               Disable auto-reset");
        Console.WriteLine("loop                   Show the current loop status");
        Console.WriteLine("quit                   Close the tool");
    }
}
