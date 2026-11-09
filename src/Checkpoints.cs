using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

internal static partial class RepeatCorner
{
    internal const int VisibleSaveRows = 5;
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    private static readonly string SaveDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Assetto Corsa Competizione", "Savegames");

    public sealed class ToolSettings
    {
        public List<Checkpoint> Checkpoints = new List<Checkpoint>();
        public List<Section> Sections = new List<Section>();
        public string ActiveCheckpoint;
        public WheelBinding Wheel;
        public string Overlay;
    }

    public sealed class Checkpoint
    {
        public string Name, Slot, Created, Track;
    }

    internal sealed class Save
    {
        public string Slot, Created, Label, Mode, Track, TrackDisplayName;
        public int Row;

        public bool SameSave(Save other)
        {
            return Slot == other.Slot && Created == other.Created && Track == other.Track;
        }

        public bool MatchesPin(Checkpoint pin)
        {
            return pin != null && Slot == pin.Slot && Created == pin.Created && Track == pin.Track;
        }
    }

    private static Save[] ReadSaves()
    {
        if (!Directory.Exists(SaveDirectory))
            return new Save[0];

        var saves = Directory.EnumerateFiles(SaveDirectory, "*H.json")
            .Where(path => Regex.IsMatch(Path.GetFileName(path), @"^[0-9]+H\.json$"))
            .Select(ReadSave)
            .OrderByDescending(save => save.Created, StringComparer.Ordinal)
            .ToArray();
        for (int row = 0; row < saves.Length; row++)
            saves[row].Row = row;
        return saves;
    }

    internal sealed class SavedRaceState
    {
        public SavedCarState[] CarStates { get; set; }
    }

    internal sealed class SavedCarState
    {
        public SavedCarCache CarCache { get; set; }
    }

    internal sealed class SavedCarCache
    {
        public double? LerpSplinePosition { get; set; }
    }

    private static double? ReadLoadPosition(string path)
    {
        try
        {
            using (var file = File.OpenRead(path))
            {
                // ACC can write the race state in UTF-16 LE without a byte-order mark.
                file.ReadByte();
                var encoding = file.ReadByte() == 0 ? Encoding.Unicode : Encoding.UTF8;
                file.Position = 0;
                using (var reader = new StreamReader(file, encoding))
                {
                    var state = Json.Deserialize<SavedRaceState>(reader.ReadToEnd());
                    // Repeat Corner works with solo practice. A single car makes the saved position unambiguous.
                    if (state == null || state.CarStates == null || state.CarStates.Length != 1) return null;
                    var car = state.CarStates[0];
                    double? position = car == null || car.CarCache == null ? null : car.CarCache.LerpSplinePosition;
                    return position >= 0 && position <= 1 ? position : null;
                }
            }
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (FormatException) { return null; }
    }

    private static Save ReadSave(string path)
    {
        var data = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
        string created = Convert.ToString(data["creationDate"], CultureInfo.InvariantCulture);
        DateTime parsed;
        if (!DateTime.TryParseExact(created, "yyyy.MM.dd-HH.mm.ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            throw new InvalidOperationException("Could not read the date of ACC save " + Path.GetFileName(path) + ".");
        string trackDisplayName = Convert.ToString(data["trackDisplayName"]);
        return new Save
        {
            Slot = Path.GetFileNameWithoutExtension(path).TrimEnd('H'),
            Created = created,
            Mode = Convert.ToString(data["gameMode"]),
            Track = Convert.ToString(data["trackName"]),
            TrackDisplayName = trackDisplayName,
            Label = trackDisplayName + " (" + created + ")"
        };
    }

    private static Save SelectSave()
    {
        return SelectSave(ReadSaves(), store.ActiveCheckpoint());
    }

    internal static Save SelectSave(Save[] saves, Checkpoint pin)
    {
        var selected = pin == null ? saves.FirstOrDefault() :
            saves.FirstOrDefault(save => save.MatchesPin(pin));
        if (selected == null)
            throw new InvalidOperationException(pin == null ? "Create a Free Practice save in ACC first." :
                "The save for '" + pin.Name + "' was removed or replaced. Select another pin or choose Latest save.");
        if (selected.Mode != "Practice")
            throw new InvalidOperationException("Reset requires a Free Practice save.");
        if (selected.Row >= VisibleSaveRows)
            throw new InvalidOperationException("The pinned save is below the five visible Saved Games rows.");
        if (saves.Count(save => save.Created == selected.Created) != 1)
            throw new InvalidOperationException("Multiple saves share this timestamp. Save again to create a save ACC Repeat can tell apart.");
        return selected;
    }

    private static void PrintSaves()
    {
        var saves = ReadSaves();
        if (saves.Length == 0) Console.WriteLine("No ACC saved games found.");
        foreach (var save in saves)
            Console.WriteLine("{0}. {1} [{2}]{3}", save.Row + 1, save.Label, save.Mode,
                save.Row >= VisibleSaveRows ? " (outside visible rows)" : "");
    }

    private static void PinSave(string arguments)
    {
        string[] parts = arguments.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        int number;
        if (parts.Length != 2 || !int.TryParse(parts[0], out number))
            throw new InvalidOperationException("Usage: pin <save number> <name>. Type 'saves' to see the numbers.");
        var saves = ReadSaves();
        if (number < 1 || number > Math.Min(VisibleSaveRows, saves.Length) || saves[number - 1].Mode != "Practice")
            throw new InvalidOperationException("Choose a visible Free Practice save.");
        PinCheckpoint(saves, saves[number - 1], parts[1]);
    }

    private static void PinCheckpoint(Save[] saves, Save selected, string name)
    {
        store.SetCheckpointName(saves, selected, name);
        store.Save();
        DisableLoop("the active save changed");
        ReportStatus("Active save: " + store.Settings.ActiveCheckpoint);
    }

    private static void UseCheckpoint(string name)
    {
        var pin = store.FindCheckpoint(name);
        if (pin == null)
            throw new InvalidOperationException("Unknown pin. Type 'pins' to list names.");
        store.Settings.ActiveCheckpoint = pin.Name;
        store.Save();
        DisableLoop("the active save changed");
        ReportStatus("Active save: " + pin.Name);
    }

    private static void UseLatest()
    {
        store.Settings.ActiveCheckpoint = null;
        store.Save();
        DisableLoop("the active save changed");
        ReportStatus("Reset will use the newest save if it is from Free Practice.");
    }

    private static void PrintCheckpoints()
    {
        Console.WriteLine("Active save: " + (store.Settings.ActiveCheckpoint ?? "latest save"));
        foreach (var pin in store.Settings.Checkpoints)
            Console.WriteLine("  {0} - {1} ({2})", pin.Name, pin.Track, pin.Created);
    }
}
