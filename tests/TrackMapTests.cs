using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using static RepeatCorner;

namespace ACCRepeat.Tests
{
    public class TrackMapTests
    {
        private const double Radius = 900;

        private static TrackMap Circle(int samples, int bins)
        {
            var map = new TrackMap { Track = "monza" };
            for (int sample = 0; sample < samples; sample++)
            {
                double position = (sample + 0.5) / bins, angle = position * 2 * Math.PI;
                map.Add(position, Radius * Math.Cos(angle), Radius * Math.Sin(angle));
            }
            return map;
        }

        private static TrackMapView FullLap()
        {
            return TrackMapView.From("monza", Circle(3 * MapBins, 3 * MapBins));
        }

        [Fact]
        public void FullLapCompletesTheMap()
        {
            Assert.Equal(1, FullLap().Coverage);
        }

        [Fact]
        public void ClickBesideTheTrackSnapsToTheNearestLapPosition()
        {
            double gap;
            var picked = FullLap().Nearest(Radius * Math.Cos(0.5 * Math.PI) * 1.02, Radius * Math.Sin(0.5 * Math.PI) * 1.02, out gap);
            Assert.True(picked.HasValue);
            Assert.InRange(picked.Value, 0.249, 0.251);
            Assert.InRange(gap, Radius * 0.02 - 1, Radius * 0.02 + 1);
        }

        [Fact]
        public void LapPositionIsDrawnInTheRightPlace()
        {
            var point = FullLap().PointAt(0.25).Value;
            Assert.InRange(point.X, -5, 5);
            Assert.InRange(point.Y, Radius - 5, Radius + 5);
        }

        [Fact]
        public void PickingWorksAcrossTheStartLine()
        {
            double gap;
            var picked = FullLap().Nearest(Radius, -3, out gap);
            Assert.True(picked.HasValue && (picked.Value < 0.001 || picked.Value > 0.999));
        }

        [Fact]
        public void HalfDrawnMapOnlyPicksRecordedPoints()
        {
            var view = TrackMapView.From("monza", Circle(MapBins / 2, MapBins));
            double gap;
            var picked = view.Nearest(Radius * Math.Cos(1.5 * Math.PI), Radius * Math.Sin(1.5 * Math.PI), out gap);
            Assert.Equal(0.5, view.Coverage, 6);
            Assert.True(picked.HasValue && picked.Value <= 0.5);
        }

        [Fact]
        public void TrackMapRoundTripsThroughJson()
        {
            var half = Circle(MapBins / 2, MapBins);
            var restored = Json.Deserialize<List<TrackMap>>(Json.Serialize(new[] { half }));
            Assert.Single(restored);
            Assert.Equal(MapBins / 2, restored[0].Filled.Count(filled => filled));
            Assert.Equal(half.X[10], restored[0].X[10]);
        }
    }

    public sealed class TrackMapImportTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        private readonly string path;

        public TrackMapImportTests()
        {
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "monza.bin");
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(new byte[] { 0x72, 0x65, 0x74, 0x6d });
                writer.Write((ushort)1);
                writer.Write(0L);
                for (int sample = MapBins - 1; sample >= 0; sample--)
                {
                    float position = (sample + 0.25f) / MapBins;
                    double angle = position * 2 * Math.PI;
                    writer.Write((float)(900 * Math.Cos(angle)));
                    writer.Write((float)(900 * Math.Sin(angle)));
                    writer.Write(position);
                }
            }
        }

        public void Dispose()
        {
            Directory.Delete(directory, true);
        }

        [Fact]
        public void CompleteImportFillsTheLap()
        {
            var map = ReadTrackMapFile("monza", path);
            Assert.True(map.Imported);
            Assert.Equal(1, TrackMapView.From("monza", map).Coverage);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(0.0405)]
        [InlineData(0.1807)]
        public void ImportedPointPicksTheSameLapPosition(double position)
        {
            var view = TrackMapView.From("monza", ReadTrackMapFile("monza", path));
            var point = view.PointAt(position).Value;
            double gap;
            var picked = view.Nearest(point.X, point.Y, out gap);
            Assert.True(picked.HasValue);
            Assert.True(Math.Min(Math.Abs(position - picked.Value), 1 - Math.Abs(position - picked.Value)) < 0.00001);
        }

        [Fact]
        public void ImportRejectsAnotherCircuitsMap()
        {
            Assert.Throws<InvalidDataException>(() => ReadTrackMapFile("spa", path));
        }

        [Fact]
        public void ImportRejectsAMapWithALargeGap()
        {
            using (var stream = File.OpenWrite(path)) stream.SetLength(stream.Length - 12 * 50);
            Assert.Throws<InvalidDataException>(() => ReadTrackMapFile("monza", path));
        }
    }

    [Collection(EngineStatics.Name)]
    public class BundledTrackMapTests
    {
        public static IEnumerable<object[]> BundledTracks()
        {
            return Directory.GetFiles(BundledMapsPath, "*.bin")
                .Select(file => new object[] { Path.GetFileNameWithoutExtension(file) });
        }

        [Fact]
        public void AllTwentyFiveMapsAreBundled()
        {
            Assert.Equal(25, Directory.GetFiles(BundledMapsPath, "*.bin").Length);
        }

        [Theory]
        [MemberData(nameof(BundledTracks))]
        public void BundledMapLoadsCompletely(string track)
        {
            Assert.Equal(1, MapView(track).Coverage);
        }

        [Theory]
        [InlineData(0.0405)]
        [InlineData(0.1807)]
        public void BundledMonzaMapKeepsTimingMarkersInPlace(double position)
        {
            var monza = MapView("monza");
            var point = monza.PointAt(position).Value;
            double gap;
            var picked = monza.Nearest(point.X, point.Y, out gap);
            Assert.True(picked.HasValue);
            Assert.InRange(picked.Value, position - 0.00001, position + 0.00001);
        }

        [Fact]
        public void NurburgringLayoutsHaveSeparateMaps()
        {
            Assert.NotEqual(MapView("nurburgring").PointAt(0.5), MapView("nurburgring_24h").PointAt(0.5));
        }

        [Fact]
        public void UnknownTrackLoadsNoMap()
        {
            Assert.True(MapView("unmapped_track").IsEmpty);
        }

        [Fact]
        public void PartialRecordingDoesNotHideTheBundledMapButAnImportDoes()
        {
            var recorded = new TrackMap { Track = "monza" };
            var custom = new TrackMap { Track = "monza", Imported = true };
            trackMaps.Add(recorded);
            try
            {
                Assert.NotSame(recorded, FindTrackMap("monza"));
                trackMaps.Add(custom);
                Assert.Same(custom, FindTrackMap("monza"));
            }
            finally
            {
                trackMaps.Remove(custom);
                trackMaps.Remove(recorded);
            }
        }
    }
}
