using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

internal static partial class RepeatCorner
{
    private const int VisibleSaveRows = 5;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RepeatCorner.settings.json");
    private static ToolSettings settings = new ToolSettings();

    public sealed class ToolSettings
    {
        public List<Checkpoint> Checkpoints = new List<Checkpoint>();
        public List<Section> Sections = new List<Section>();
        public string ActiveCheckpoint;
        public WheelBinding Wheel;
    }

    public sealed class Checkpoint
    {
        public string Name, Slot, Created, Track;
    }

    private sealed class Save
    {
        public string Slot, Created, Label, Mode, Track, TrackDisplayName;
        public int Row;

        public bool SameSave(Save other)
        {
            return Slot == other.Slot && Created == other.Created && Track == other.Track;
        }
    }

    private static void LoadSettings()
    {
        settings = File.Exists(SettingsPath)
            ? Json.Deserialize<ToolSettings>(File.ReadAllText(SettingsPath))
            : new ToolSettings();
        if (settings == null || settings.Checkpoints == null || settings.Sections == null)
            throw new InvalidOperationException("RepeatCorner.settings.json is invalid.");
    }

    private static void StoreSettings()
    {
        string pending = SettingsPath + ".tmp";
        File.WriteAllText(pending, Json.Serialize(settings));
        if (File.Exists(SettingsPath))
            File.Replace(pending, SettingsPath, null);
        else
            File.Move(pending, SettingsPath);
    }

    private static Save[] ReadSaves()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Assetto Corsa Competizione", "Savegames");
        if (!Directory.Exists(folder))
            return new Save[0];

        var saves = Directory.EnumerateFiles(folder, "*H.json")
            .Where(path => Regex.IsMatch(Path.GetFileName(path), @"^[0-9]+H\.json$"))
            .Select(ReadSave)
            .OrderByDescending(save => save.Created, StringComparer.Ordinal)
            .ToArray();
        for (int row = 0; row < saves.Length; row++)
            saves[row].Row = row;
        return saves;
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

    private static Checkpoint ActiveCheckpoint()
    {
        if (string.IsNullOrEmpty(settings.ActiveCheckpoint)) return null;
        var pin = FindCheckpoint(settings.ActiveCheckpoint);
        if (pin == null)
            throw new InvalidOperationException("The active pin is missing. Choose Latest save or select another pin.");
        return pin;
    }

    private static Checkpoint FindCheckpoint(string name)
    {
        return settings.Checkpoints.Find(pin => string.Equals(pin.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static Save SelectSave()
    {
        return SelectSave(ReadSaves(), ActiveCheckpoint());
    }

    private static Save SelectSave(Save[] saves, Checkpoint pin)
    {
        var selected = pin == null ? saves.FirstOrDefault() :
            saves.FirstOrDefault(save => save.Slot == pin.Slot && save.Created == pin.Created && save.Track == pin.Track);
        if (selected == null)
            throw new InvalidOperationException(pin == null ? "Create a Free Practice save in ACC first." :
                "The save for '" + pin.Name + "' was removed or replaced. Select another pin or choose Latest save.");
        if (selected.Mode != "Practice")
            throw new InvalidOperationException("Retry requires a Free Practice save.");
        if (selected.Row >= VisibleSaveRows)
            throw new InvalidOperationException("The checkpoint is below the five visible Saved Games rows.");
        if (saves.Count(save => save.Created == selected.Created) != 1)
            throw new InvalidOperationException("Multiple saves share this timestamp. Save again to create a distinguishable checkpoint.");
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
        name = name.Trim();
        if (name.Length == 0 || FindCheckpoint(name) != null)
            throw new InvalidOperationException("Choose a new, non-empty checkpoint name.");
        var pin = new Checkpoint { Name = name, Slot = selected.Slot, Created = selected.Created, Track = selected.Track };
        SelectSave(saves, pin);
        settings.Checkpoints.Add(pin);
        settings.ActiveCheckpoint = name;
        StoreSettings();
        DisableLoop("the active checkpoint changed");
        ReportStatus("Active checkpoint: " + name);
    }

    private static void UseCheckpoint(string name)
    {
        var pin = FindCheckpoint(name);
        if (pin == null)
            throw new InvalidOperationException("Unknown checkpoint. Type 'checkpoints' to list names.");
        settings.ActiveCheckpoint = pin.Name;
        StoreSettings();
        DisableLoop("the active checkpoint changed");
        ReportStatus("Active checkpoint: " + pin.Name);
    }

    private static void UseLatest()
    {
        settings.ActiveCheckpoint = null;
        StoreSettings();
        DisableLoop("the active checkpoint changed");
        ReportStatus("Retry will use the newest save if it is from Free Practice.");
    }

    private static void PrintCheckpoints()
    {
        Console.WriteLine("Active checkpoint: " + (settings.ActiveCheckpoint ?? "latest save"));
        foreach (var pin in settings.Checkpoints)
            Console.WriteLine("  {0} - {1} ({2})", pin.Name, pin.Track, pin.Created);
    }
}
