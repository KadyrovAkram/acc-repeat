using System;
using Xunit;
using static RepeatCorner;

namespace ACCRepeat.Tests
{
    public class CutDetectionTests
    {
        private readonly Section section = new Section { Track = "monza", Start = 0.04, Finish = 0.18 };

        public CutDetectionTests()
        {
            foreach (double path in new[] { 812.0, 809.0, 814.0 })
                section.RecentAttempts.Add(new Attempt { Seconds = 29, Path = path });
            section.RecentAttempts.Add(new Attempt { Seconds = 29 });
        }

        [Fact]
        public void NothingIsACutWithoutEarlierRuns()
        {
            var empty = new Section { Track = "monza", Start = 0.04, Finish = 0.18 };
            Assert.Null(CutMetres(empty, new Attempt { Seconds = 28, Path = 700 }));
        }

        [Fact]
        public void SlightlyTighterLineIsNotACut()
        {
            Assert.Null(CutMetres(section, new Attempt { Seconds = 28.9, Path = 806 }));
        }

        [Fact]
        public void RunningWideIsNotACut()
        {
            Assert.Null(CutMetres(section, new Attempt { Seconds = 28.5, Path = 830 }));
        }

        [Fact]
        public void ChicaneCutIsDetectedWithTheMetresSaved()
        {
            var cut = CutMetres(section, new Attempt { Seconds = 27.4, Path = 790 });
            Assert.True(cut.HasValue);
            Assert.Equal(22, cut.Value, 6);
        }

        [Fact]
        public void RunWithoutMeasuredDistanceIsNotACut()
        {
            Assert.Null(CutMetres(section, new Attempt { Seconds = 27.4 }));
        }
    }

    public class RunRemovalTests
    {
        private readonly Section section = new Section { Track = "monza", Start = 0.04, Finish = 0.18 };

        public RunRemovalTests()
        {
            // Two cuts recorded first, while there was nothing to compare against.
            foreach (var run in new[]
            {
                new Attempt { Seconds = 28.9, Path = 811 }, new Attempt { Seconds = 28.7, Path = 812 },
                new Attempt { Seconds = 29.1, Path = 810 }, new Attempt { Seconds = 27.4, Path = 789 },
                new Attempt { Seconds = 27.6, Path = 791 }
            })
                RecordAttempt(section, run);
        }

        [Fact]
        public void EarlierCutsArePrunedOnceTheUsualLineIsKnown()
        {
            Assert.Equal(27.4, section.BestAttempt.Seconds);
            Assert.Equal(2, PruneCuts(section));
            Assert.Equal(3, section.RecentAttempts.Count);
            Assert.Equal(28.7, section.BestAttempt.Seconds);
            Assert.Equal(0, PruneCuts(section));
        }

        [Fact]
        public void RemovingThePersonalBestPromotesTheNextQuickestRun()
        {
            PruneCuts(section);
            // A best restored from the settings file is a separate object with the same values.
            section.BestAttempt = Json.Deserialize<Attempt>(Json.Serialize(section.BestAttempt));

            Assert.True(DropAttempt(section, section.RecentAttempts.Find(run => run.Seconds == 28.7)));
            Assert.Equal(28.9, section.BestAttempt.Seconds);
            Assert.Equal(2, section.RecentAttempts.Count);
        }

        [Fact]
        public void RemovingASlowerRunKeepsThePersonalBest()
        {
            PruneCuts(section);
            Assert.False(DropAttempt(section, section.RecentAttempts.Find(run => run.Seconds == 29.1)));
            Assert.Equal(28.7, section.BestAttempt.Seconds);
        }

        [Fact]
        public void RemovingARunNotInTheHistoryThrows()
        {
            Assert.Throws<InvalidOperationException>(() => DropAttempt(section, new Attempt { Seconds = 30 }));
        }

        [Fact]
        public void RunsWithoutMeasuredDistanceAreNeverPruned()
        {
            var legacy = new Section { Track = "monza", Start = 0.04, Finish = 0.18 };
            foreach (double seconds in new[] { 29.0, 27.0, 28.0, 28.5 }) RecordAttempt(legacy, new Attempt { Seconds = seconds });
            Assert.Equal(0, PruneCuts(legacy));
            Assert.Equal(4, legacy.RecentAttempts.Count);
        }
    }

    public class AttemptHistoryTests
    {
        private readonly Attempt firstRun = new Attempt { Seconds = 0.1, EntrySpeed = 110, ExitSpeed = 130 };
        private readonly Section section;

        public AttemptHistoryTests()
        {
            var legacy = Json.Deserialize<ToolSettings>(
                "{\"ActiveCheckpoint\":\"Monza T1\",\"Sections\":[{\"Checkpoint\":\"Monza T1\",\"Track\":\"monza\",\"Start\":0.04,\"Finish\":0.18}]}");
            section = legacy.Sections[0];
        }

        [Fact]
        public void SectionSavedWithoutHistoryUpgradesCleanly()
        {
            Assert.Empty(section.RecentAttempts);
            Assert.Null(section.BestAttempt);
            Assert.Equal(0.18, section.Finish);
        }

        [Fact]
        public void FirstRunHasNoPreviousBestDelta()
        {
            Assert.Null(RecordAttempt(section, firstRun));
        }

        [Fact]
        public void HistoryKeepsTheFiveLatestRunsAndAnIndependentBest()
        {
            RecordAttempt(section, firstRun);
            for (int seconds = 11; seconds <= 16; seconds++)
                Assert.True(RecordAttempt(section, new Attempt { Seconds = seconds }) > 0);

            Assert.Equal(5, section.RecentAttempts.Count);
            Assert.Equal(16, section.RecentAttempts[0].Seconds);
            Assert.Equal(12, section.RecentAttempts[4].Seconds);
            Assert.Same(firstRun, section.BestAttempt);
        }

        [Fact]
        public void HistorySurvivesASettingsRoundTrip()
        {
            RecordAttempt(section, firstRun);
            for (int seconds = 11; seconds <= 16; seconds++) RecordAttempt(section, new Attempt { Seconds = seconds });
            var restored = Json.Deserialize<Section>(Json.Serialize(section));

            Assert.Equal(5, restored.RecentAttempts.Count);
            Assert.Equal(firstRun.EntrySpeed, restored.BestAttempt.EntrySpeed);
            Assert.Equal(16 - firstRun.Seconds, restored.RecentAttempts[0].PreviousBestDelta);
        }

        [Fact]
        public void FasterRunReplacesTheBestWithANegativeDelta()
        {
            RecordAttempt(section, firstRun);
            var faster = new Attempt { Seconds = firstRun.Seconds / 2 };
            Assert.True(RecordAttempt(section, faster) < 0);
            Assert.Same(faster, section.BestAttempt);
        }

        [Fact]
        public void ReconfirmingTheSameFinishKeepsHistory()
        {
            RecordAttempt(section, firstRun);
            SetFinish(section, 0.18);
            Assert.Same(firstRun, section.BestAttempt);
        }

        [Fact]
        public void MovingTheFinishClearsHistory()
        {
            RecordAttempt(section, firstRun);
            SetFinish(section, 0.20);
            Assert.Empty(section.RecentAttempts);
            Assert.Null(section.BestAttempt);
        }
    }
}
