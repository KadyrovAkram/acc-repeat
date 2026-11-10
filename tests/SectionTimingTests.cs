using System.Drawing;
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

        [Theory]
        [InlineData(0.101)]
        [InlineData(0.100999)]
        public void DistanceIncludesFramesWithoutForwardLapProgress(double position)
        {
            timer.Observe(normal, Frame(1, 0.099, 0.0, 0, 72));
            timer.Observe(normal, Frame(2, 0.101, 0.1, 2, 72));
            timer.Observe(normal, Frame(3, position, 0.2, 2, 72));
            var result = timer.Observe(normal, Frame(4, 0.103, 0.3, 4, 72));

            Assert.NotNull(result);
            Assert.Equal(result.Seconds * 20, result.Path, Precision);
        }

        [Theory]
        [InlineData(8, 8, false)]
        [InlineData(0, 8, false)]
        [InlineData(0, 0, true)]
        public void WheelCheckRequiresAllFourTyresOutside(double threeTyres, double fourthTyre, bool expectedInvalid)
        {
            var limits = new TrackLimits(Square(12), Square(5));
            var section = new Section { Track = "brands_hatch", Start = 0.1, Finish = 0.105 };
            Attempt result = null;
            for (int index = 0; index < 8; index++)
            {
                var frame = Frame(index, 0.099 + index * 0.001, index * 0.05, index, 72);
                frame.Tyres = index >= 2 && index <= 5 ? Tyres(threeTyres, fourthTyre) : Tyres(8, 8);
                result = timer.Observe(section, frame, limits) ?? result;
            }

            Assert.NotNull(result);
            Assert.Equal(expectedInvalid, timer.Boundary.CutMetres > 0);

            timer.Reset();
            for (int index = 0; index < 8; index++)
            {
                var frame = Frame(index, 0.099 + index * 0.001, index * 0.05, index, 72);
                frame.Tyres = Tyres(8, 8);
                timer.Observe(section, frame, limits);
            }
            Assert.Equal(0, timer.Boundary.CutMetres);
        }

        private static PointF[] Tyres(double threeTyres, double fourthTyre)
        {
            var outside = new PointF((float)threeTyres, 0);
            return new[] { outside, outside, outside, new PointF((float)fourthTyre, 0) };
        }

        private static PointF[] Square(float halfWidth)
        {
            return new[]
            {
                new PointF(-halfWidth, -halfWidth), new PointF(halfWidth, -halfWidth),
                new PointF(halfWidth, halfWidth), new PointF(-halfWidth, halfWidth)
            };
        }
    }
}
