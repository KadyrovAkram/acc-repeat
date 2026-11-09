using System;
using System.IO;
using Xunit;
using static RepeatCorner;

namespace ACCRepeat.Tests
{
    public sealed class SettingsStoreTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        private readonly string path;

        public SettingsStoreTests()
        {
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "ACCRepeat.settings.json");
        }

        public void Dispose()
        {
            Directory.Delete(directory, true);
        }

        [Fact]
        public void MissingFileLoadsEmptySettings()
        {
            var store = new SettingsStore(path);
            store.Load();
            Assert.Empty(store.Settings.Checkpoints);
            Assert.Null(store.Settings.ActiveCheckpoint);
        }

        [Fact]
        public void SavedSettingsLoadIntoANewStore()
        {
            var store = new SettingsStore(path);
            store.Settings.Checkpoints.Add(new Checkpoint { Name = "Monza T1", Slot = "4", Created = "old", Track = "monza" });
            store.Settings.ActiveCheckpoint = "Monza T1";
            store.Save();
            store.Settings.Overlay = "Off";
            store.Save();

            var reloaded = new SettingsStore(path);
            reloaded.Load();
            Assert.Equal("Monza T1", reloaded.ActiveCheckpoint().Name);
            Assert.Equal("Off", reloaded.Settings.Overlay);
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public void LoadingCollapsesDuplicatePins()
        {
            File.WriteAllText(path, "{\"ActiveCheckpoint\":\"B\",\"Sections\":[],\"Checkpoints\":[" +
                "{\"Name\":\"A\",\"Slot\":\"4\",\"Created\":\"old\",\"Track\":\"monza\"}," +
                "{\"Name\":\"B\",\"Slot\":\"4\",\"Created\":\"old\",\"Track\":\"monza\"}]}");
            var store = new SettingsStore(path);
            store.Load();
            Assert.Single(store.Settings.Checkpoints);
            Assert.Equal("B", store.ActiveCheckpoint().Name);
        }

        [Fact]
        public void FileWithoutPinListsIsRejected()
        {
            File.WriteAllText(path, "{\"Checkpoints\":null}");
            var error = Assert.Throws<InvalidOperationException>(() => new SettingsStore(path).Load());
            Assert.Equal("ACCRepeat.settings.json is invalid.", error.Message);
        }

        [Fact]
        public void RestoreUndoesChangesSinceTheSnapshot()
        {
            var store = new SettingsStore(path);
            store.Settings.ActiveCheckpoint = "Before";
            string snapshot = store.Snapshot();
            store.Settings.ActiveCheckpoint = "After";
            store.Settings.Sections.Add(new Section { Track = "monza", Start = 0.1 });
            store.Restore(snapshot);
            Assert.Equal("Before", store.Settings.ActiveCheckpoint);
            Assert.Empty(store.Settings.Sections);
        }

        [Fact]
        public void MissingActivePinIsReported()
        {
            var store = new SettingsStore(path, new ToolSettings { ActiveCheckpoint = "Gone" });
            Assert.Throws<InvalidOperationException>(() => store.ActiveCheckpoint());
        }
    }
}
