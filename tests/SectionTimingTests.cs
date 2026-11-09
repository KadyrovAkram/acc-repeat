using Xunit;
using static ACCRepeat.Tests.Telemetry;
using static RepeatCorner;

namespace ACCRepeat.Tests
{
    public class SectionTimingTests
    {
        private const int Precision = 6;
        private readonly SectionTimer timer = new SectionTimer();
        private readonly Section normal = new Section { Track = "monza", Start = 0.1, Finish = 0.102 };

        [Fact]
        public void CrossingTimesAreInterpolatedAcrossTheLapWrap()
        {
            var section = new Section { Track = "monza", Start = 0.9995, Finish = 0.0005 };
            timer.Observe(section, Frame(1, 0.999, 0, 0, 100));
            timer.Observe(section, Frame(2, 0.000, 0.1, 3, 120));
            var result = timer.Observe(section, Frame(3, 0.001, 0.2, 6, 140));

            Assert.NotNull(result);
            Assert.Equal(0.1, result.Seconds, Precision);
            Assert.Equal(110, result.EntrySpeed, Precision);
            Assert.Equal(130, result.ExitSpeed, Precision);
            // Half of each 0.1 s frame at 110 km/h and 130 km/h average speed.
            Assert.Equal((110 + 130) / 3.6 * 0.05, result.Path, Precision);
        }

        [Fact]
        public void TeleportAcrossTheFinishIsNotAnAttempt()
        {
            timer.Observe(normal, Frame(1, 0.099, 0, 0, 100));
            timer.Observe(normal, Frame(2, 0.101, 0.1, 3, 105));
            Assert.Null(timer.Observe(normal, Frame(3, 0.103, 0.2, 200, 120)));
        }

        [Fact]
        public void RunInterruptedMidSectionDoesNotCountAfterResuming()
        {
            timer.Observe(normal, Frame(1, 0.099, 0, 0, 100));
            timer.Observe(normal, Frame(2, 0.101, 0.1, 3, 105));
            timer.Observe(normal, null);
            timer.Observe(normal, Frame(3, 0.101, 0.2, 3, 105));
            Assert.Null(timer.Observe(normal, Frame(4, 0.103, 0.3, 6, 120)));
        }

        [Fact]
        public void DrivenDistanceIsSummedAcrossFrames()
        {
            var longer = new Section { Track = "monza", Start = 0.1, Finish = 0.106 };
            timer.Observe(longer, Frame(1, 0.099, 0.0, 0, 72));
            timer.Observe(longer, Frame(2, 0.101, 0.1, 2, 72));
            timer.Observe(longer, Frame(3, 0.103, 0.2, 4, 72));
            timer.Observe(longer, Frame(4, 0.105, 0.3, 6, 72));
            var steady = timer.Observe(longer, Frame(5, 0.107, 0.4, 8, 72));

            Assert.NotNull(steady);
            Assert.Equal(6, steady.Path, Precision);
        }
    }
}
