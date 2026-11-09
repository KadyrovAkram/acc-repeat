using System;
using System.IO;
using System.Linq;

internal static partial class RepeatCorner
{
    // Owns the pins, timing lines, run history and preferences, and reads and writes them as
    // ACCRepeat.settings.json. The engine thread is the only writer; the window sees copies
    // of these values through DeskSnapshot.
    internal sealed class SettingsStore
    {
        internal static readonly string DefaultPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ACCRepeat.settings.json");

        private readonly string path;
        private ToolSettings settings;

        public SettingsStore(string path) : this(path, new ToolSettings()) { }

        public SettingsStore(string path, ToolSettings settings)
        {
            this.path = path;
            this.settings = settings;
        }

        public ToolSettings Settings { get { return settings; } }

        public void Load()
        {
            settings = File.Exists(path)
                ? Json.Deserialize<ToolSettings>(File.ReadAllText(path))
                : new ToolSettings();
            if (settings == null || settings.Checkpoints == null || settings.Sections == null)
                throw new InvalidOperationException("ACCRepeat.settings.json is invalid.");
            CollapseDuplicatePins();
        }

        public void Save()
        {
            string pending = path + ".tmp";
            File.WriteAllText(pending, Json.Serialize(settings));
            if (File.Exists(path))
                File.Replace(pending, path, null);
            else
                File.Move(pending, path);
        }

        // A command that fails part-way puts the settings back the way they were.
        public string Snapshot() { return Json.Serialize(settings); }

        public void Restore(string snapshot) { settings = Json.Deserialize<ToolSettings>(snapshot); }

        public void CollapseDuplicatePins()
        {
            var groups = settings.Checkpoints.GroupBy(pin => new { pin.Slot, pin.Created, pin.Track }).ToArray();
            var active = FindCheckpoint(settings.ActiveCheckpoint);
            foreach (var group in groups.Where(group => group.Count() > 1))
            {
                var names = group.Select(pin => pin.Name).ToArray();
                var sections = settings.Sections.Where(section => section.Checkpoint != null &&
                    names.Contains(section.Checkpoint, StringComparer.OrdinalIgnoreCase)).ToArray();
                // Older versions let separate drills share a save. Preserve pins that each have timing data.
                if (sections.Select(section => section.Checkpoint).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                    continue;

                var keep = group.Contains(active) ? active : group.Last();
                foreach (var section in sections)
                    section.Checkpoint = keep.Name;
                settings.Checkpoints.RemoveAll(pin => pin != keep && group.Contains(pin));
                if (keep == active)
                    settings.ActiveCheckpoint = keep.Name;
            }
        }

        public Checkpoint ActiveCheckpoint()
        {
            if (string.IsNullOrEmpty(settings.ActiveCheckpoint)) return null;
            var pin = FindCheckpoint(settings.ActiveCheckpoint);
            if (pin == null)
                throw new InvalidOperationException("The active pin is missing. Choose Latest save or select another pin.");
            return pin;
        }

        public Checkpoint FindCheckpoint(string name)
        {
            return settings.Checkpoints.Find(pin => string.Equals(pin.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public Section CurrentSection(string track)
        {
            return settings.Sections.Find(section => section.Checkpoint == settings.ActiveCheckpoint && SameTrack(section.Track, track));
        }

        public void SetCheckpointName(Save[] saves, Save selected, string name)
        {
            name = name.Trim();
            var named = FindCheckpoint(name);
            if (name.Length == 0 || (named != null && !selected.MatchesPin(named)))
                throw new InvalidOperationException("Choose a non-empty name that isn't used by another save.");

            var active = FindCheckpoint(settings.ActiveCheckpoint);
            var existing = named ?? (selected.MatchesPin(active) ? active : settings.Checkpoints.LastOrDefault(selected.MatchesPin));
            var pin = existing ?? new Checkpoint { Name = name, Slot = selected.Slot, Created = selected.Created, Track = selected.Track };
            SelectSave(saves, pin);
            if (existing == null)
                settings.Checkpoints.Add(pin);
            else
            {
                foreach (var section in settings.Sections.Where(section =>
                    string.Equals(section.Checkpoint, pin.Name, StringComparison.OrdinalIgnoreCase)))
                    section.Checkpoint = name;
                pin.Name = name;
            }
            settings.ActiveCheckpoint = name;
        }
    }
}
