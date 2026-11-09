using Xunit;
using static RepeatCorner;

namespace ACCRepeat.Tests
{
    [Collection(EngineStatics.Name)]
    public class LoopCountdownTests
    {
        [Fact]
        public void CountdownWaitsForTheResultCalloutThenTicksOncePerSecond()
        {
            var pending = new LoopCountdown(10, 3);
            Assert.Null(pending.Update(11, false));
            Assert.Equal(3, pending.Update(12, true));
            Assert.Null(pending.Update(12.5, true));
            Assert.Equal(2, pending.Update(13, true));
            Assert.Equal(1, pending.Update(14, true));
            Assert.Equal(0, pending.Update(15, true));
            Assert.Null(pending.Update(16, true));
        }

        [Fact]
        public void StalledVoiceCannotBlockTheCountdown()
        {
            var stalled = new LoopCountdown(10, 2);
            Assert.Null(stalled.Update(15.9, false));
            Assert.Equal(2, stalled.Update(16, false));
            Assert.Equal(0, stalled.Update(18, false));
        }

        [Fact]
        public void LeavingACCPausesAndResumesWithTheRemainingTime()
        {
            var paused = new LoopCountdown(10, 3);
            Assert.Equal(3, paused.Update(10, true));
            Assert.True(paused.Suspend(11.5));
            Assert.False(paused.Suspend(15));
            Assert.Null(paused.Update(25, true));
            Assert.True(paused.Resume(25));
            Assert.Equal(2, paused.Update(25, true));
            Assert.Equal(1, paused.Update(25.75, true));
            Assert.Equal(0, paused.Update(26.5, true));
        }

        [Fact]
        public void SuspendingDuringTheCalloutDoesNotShortenTheCountdown()
        {
            var paused = new LoopCountdown(10, 3);
            paused.Suspend(10.5);
            Assert.Null(paused.Update(100, true));
            paused.Resume(100);
            Assert.Equal(3, paused.Update(100, true));
            Assert.Equal(0, paused.Update(103, true));
        }

        [Fact]
        public void ManualRetryCancelsOnlyThePendingCountdown()
        {
            loopSave = new Save { Slot = "4", Created = "selected", Track = "monza" };
            countdown = new LoopCountdown(10, 3);
            CancelLoopCountdown();
            Assert.Null(countdown);
            Assert.NotNull(loopSave);
        }

        [Fact]
        public void DisablingTheLoopClearsTheArmedRetry()
        {
            loopSave = new Save { Slot = "4", Created = "selected", Track = "monza" };
            countdown = new LoopCountdown(10, 3);
            DisableLoop("test cancellation");
            Assert.Null(countdown);
            Assert.Null(loopSave);
        }
    }
}
