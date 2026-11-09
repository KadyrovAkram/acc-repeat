using System;
using Xunit;
using static RepeatCorner;

namespace ACCRepeat.Tests
{
    public class SaveSelectionTests
    {
        private static Save OldSave() { return new Save { Row = 1, Slot = "4", Created = "old", Track = "monza", Mode = "Practice" }; }
        private static Save NewerSave() { return new Save { Row = 0, Slot = "5", Created = "new", Track = "monza", Mode = "Practice" }; }
        private static Checkpoint Pin() { return new Checkpoint { Name = "Monza T1", Slot = "4", Created = "old", Track = "monza" }; }

        [Fact]
        public void PinnedSaveIsSelectedEvenWhenANewerSaveExists()
        {
            var old = OldSave();
            Assert.Same(old, SelectSave(new[] { NewerSave(), old }, Pin()));
        }

        [Fact]
        public void LatestModeSelectsTheFirstSave()
        {
            var newer = NewerSave();
            Assert.Same(newer, SelectSave(new[] { newer, OldSave() }, null));
        }

        [Fact]
        public void ReplacedSaveIsRejectedAsThePinnedSave()
        {
            var replaced = OldSave();
            replaced.Created = "replacement";
            Assert.Throws<InvalidOperationException>(() => SelectSave(new[] { NewerSave(), replaced }, Pin()));
        }

        [Fact]
        public void PinAndWheelBindingRoundTripThroughSettingsJson()
        {
            var persisted = new ToolSettings { ActiveCheckpoint = "Monza T1", Wheel = new WheelBinding { Button = 31 } };
            persisted.Checkpoints.Add(Pin());
            var roundTrip = Json.Deserialize<ToolSettings>(Json.Serialize(persisted));
            Assert.Equal("old", roundTrip.Checkpoints[0].Created);
            Assert.Equal(31, roundTrip.Wheel.Button);
        }

        [Theory]
        [InlineData(1u, 0)]
        [InlineData(1u << 7, 7)]
        [InlineData(1u << 31, 31)]
        public void WheelButtonMaskDecodesToButtonIndex(uint mask, int expected)
        {
            Assert.Equal(expected, ButtonIndex(mask));
        }
    }

    public class CheckpointRenameTests
    {
        private readonly Save save = new Save { Slot = "4", Created = "old", Track = "monza", Mode = "Practice", Row = 1 };
        private readonly Save replacement = new Save { Slot = "4", Created = "new", Track = "monza", Mode = "Practice" };
        private readonly Save[] saves;
        private readonly Section section = new Section { Checkpoint = "Monza", Track = "monza", Start = 0.04, Finish = 0.18 };
        private readonly Attempt attempt = new Attempt { Seconds = 28.417 };

        public CheckpointRenameTests()
        {
            saves = new[] { replacement, save };
            settings = new ToolSettings { ActiveCheckpoint = "Monza T1" };
            settings.Checkpoints.Add(new Checkpoint { Name = "Monza", Slot = "4", Created = "old", Track = "monza" });
            settings.Checkpoints.Add(new Checkpoint { Name = "Monza T1", Slot = "4", Created = "old", Track = "monza" });
            RecordAttempt(section, attempt);
            settings.Sections.Add(section);
            CollapseDuplicatePins();
        }

        [Fact]
        public void DuplicatePinsCollapseWithoutLosingTheActivePinOrSection()
        {
            Assert.Single(settings.Checkpoints);
            Assert.Equal("Monza T1", ActiveCheckpoint().Name);
            Assert.Same(section, CurrentSection("monza"));
        }

        [Fact]
        public void RenameKeepsTimingLinesAndHistory()
        {
            SetCheckpointName(saves, save, "  First chicane  ");

            Assert.Single(settings.Checkpoints);
            Assert.Null(FindCheckpoint("Monza T1"));
            Assert.Equal("First chicane", ActiveCheckpoint().Name);
            Assert.Same(save, SelectSave(saves, ActiveCheckpoint()));
            Assert.Same(section, CurrentSection("monza"));
            Assert.Equal(0.04, section.Start);
            Assert.Equal(0.18, section.Finish);
            Assert.Same(attempt, section.BestAttempt);
            Assert.Same(attempt, section.RecentAttempts[0]);
        }

        [Fact]
        public void CaseOnlyRenameUpdatesTheActiveName()
        {
            SetCheckpointName(saves, save, "First chicane");
            SetCheckpointName(saves, save, "First Chicane");
            Assert.Single(settings.Checkpoints);
            Assert.Same(section, CurrentSection("monza"));
            Assert.Equal("First Chicane", settings.ActiveCheckpoint);
        }

        [Fact]
        public void PinningANewSaveInTheSameSlotLeavesTheExistingPinAlone()
        {
            SetCheckpointName(saves, save, "First Chicane");
            SetCheckpointName(saves, replacement, "New save");
            Assert.Equal(2, settings.Checkpoints.Count);
            Assert.Equal("old", FindCheckpoint("First Chicane").Created);
        }

        [Fact]
        public void RenameCannotTakeAnotherSavesName()
        {
            SetCheckpointName(saves, save, "First Chicane");
            SetCheckpointName(saves, replacement, "New save");
            Assert.Throws<InvalidOperationException>(() => SetCheckpointName(saves, replacement, "First Chicane"));
        }

        [Fact]
        public void CollapsingKeepsASeparateDrillOnTheSameSave()
        {
            SetCheckpointName(saves, save, "First Chicane");
            SetCheckpointName(saves, replacement, "New save");
            settings.Checkpoints.Add(new Checkpoint { Name = "Separate drill", Slot = "4", Created = "old", Track = "monza" });
            settings.Sections.Add(new Section { Checkpoint = "Separate drill", Track = "monza", Start = 0.21, Finish = 0.28 });
            CollapseDuplicatePins();
            Assert.Equal(3, settings.Checkpoints.Count);
            Assert.Equal(2, settings.Sections.Count);
        }
    }
}
