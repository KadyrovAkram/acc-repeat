using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

// ACC doesn't publish track outlines, so the tool draws its own from the car's position
// while you drive: one point per 0.1% of the lap, saved per track beside the settings.
internal static partial class RepeatCorner
{
    private const int MapBins = 1000;
    private static readonly string MapsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RepeatCorner.maps.json");
    private static List<TrackMap> trackMaps = new List<TrackMap>();
    private static bool mapsDirty;
    private static int mapVersion, cachedMapVersion = -1;
    private static double nextMapSample, nextMapSave;
    private static TrackMapView cachedMapView;

    public sealed class TrackMap
    {
        public string Track;
        public double[] X = new double[MapBins], Z = new double[MapBins];
        public bool[] Filled = new bool[MapBins];

        // Returns true when the sample fills a point the map didn't have yet.
        public bool Add(double position, double x, double z)
        {
            int bin = Math.Min(MapBins - 1, Math.Max(0, (int)(WrapPosition(position) * MapBins)));
            if (!Filled[bin])
            {
                X[bin] = x;
                Z[bin] = z;
                Filled[bin] = true;
                return true;
            }
            X[bin] += (x - X[bin]) * 0.2;
            Z[bin] += (z - Z[bin]) * 0.2;
            return false;
        }
    }

    // An immutable copy for the desk thread, with the geometry the map dialog needs.
    private sealed class TrackMapView
    {
        public string Track;
        public double[] X, Z;
        public bool[] Filled;
        public double Coverage;

        public bool IsEmpty { get { return Filled == null || Coverage == 0; } }

        public static TrackMapView From(string track, TrackMap map)
        {
            if (map == null) return new TrackMapView { Track = track };
            return new TrackMapView
            {
                Track = track, X = (double[])map.X.Clone(), Z = (double[])map.Z.Clone(),
                Filled = (bool[])map.Filled.Clone(), Coverage = map.Filled.Count(filled => filled) / (double)MapBins
            };
        }

        // Each point stands for the middle of its 0.1% slice of the lap.
        public PointF? PointAt(double position)
        {
            if (IsEmpty) return null;
            double slice = WrapPosition(position) * MapBins - 0.5;
            int index = ((int)Math.Floor(slice) + MapBins) % MapBins, next = (index + 1) % MapBins;
            double fraction = slice - Math.Floor(slice);
            if (Filled[index] && Filled[next])
                return new PointF((float)(X[index] + (X[next] - X[index]) * fraction), (float)(Z[index] + (Z[next] - Z[index]) * fraction));
            for (int offset = 0; offset < MapBins / 2; offset++)
            {
                if (Filled[(index - offset + MapBins) % MapBins]) { int at = (index - offset + MapBins) % MapBins; return new PointF((float)X[at], (float)Z[at]); }
                if (Filled[(next + offset) % MapBins]) { int at = (next + offset) % MapBins; return new PointF((float)X[at], (float)Z[at]); }
            }
            return null;
        }

        // The lap position on the drawn track closest to a world point, and how far away it is.
        public double? Nearest(double x, double z, out double distance)
        {
            distance = double.MaxValue;
            double? best = null;
            if (IsEmpty) return null;
            for (int index = 0; index < MapBins; index++)
            {
                int next = (index + 1) % MapBins;
                if (!Filled[index] || !Filled[next]) continue;
                double dx = X[next] - X[index], dz = Z[next] - Z[index];
                double length = dx * dx + dz * dz;
                double t = length == 0 ? 0 : Math.Max(0, Math.Min(1, ((x - X[index]) * dx + (z - Z[index]) * dz) / length));
                double px = X[index] + dx * t - x, pz = Z[index] + dz * t - z;
                double gap = Math.Sqrt(px * px + pz * pz);
                if (gap < distance)
                {
                    distance = gap;
                    best = WrapPosition((index + 0.5 + t) / MapBins);
                }
            }
            return best;
        }
    }

    private static TrackMap FindTrackMap(string track)
    {
        return trackMaps.Find(map => SameTrack(map.Track, track));
    }

    // Engine thread: a copy is made only when the map gains points or the track changes.
    private static TrackMapView MapView(string track)
    {
        if (track == null) return null;
        if (cachedMapView != null && cachedMapVersion == mapVersion && SameTrack(cachedMapView.Track, track))
            return cachedMapView;
        cachedMapVersion = mapVersion;
        cachedMapView = TrackMapView.From(track, FindTrackMap(track));
        return cachedMapView;
    }

    private static void LoadTrackMaps()
    {
        try
        {
            trackMaps = File.Exists(MapsPath) ? Json.Deserialize<List<TrackMap>>(File.ReadAllText(MapsPath)) : null;
        }
        catch (Exception error)
        {
            trackMaps = null;
            ReportStatus("Could not read the track maps. They will be drawn again as you drive. " + error.Message);
        }
        trackMaps = (trackMaps ?? new List<TrackMap>()).Where(map => map != null && map.Track != null &&
            map.X != null && map.Z != null && map.Filled != null && map.X.Length == MapBins &&
            map.Z.Length == MapBins && map.Filled.Length == MapBins).ToList();
        mapVersion++;
    }

    private static void SaveTrackMaps()
    {
        if (!mapsDirty) return;
        try
        {
            string pending = MapsPath + ".tmp";
            File.WriteAllText(pending, Json.Serialize(trackMaps));
            if (File.Exists(MapsPath)) File.Replace(pending, MapsPath, null);
            else File.Move(pending, MapsPath);
            mapsDirty = false;
        }
        catch (IOException error) { ReportStatus("Could not save the track map. " + error.Message); }
        catch (UnauthorizedAccessException error) { ReportStatus("Could not save the track map. " + error.Message); }
    }

    private static void PollTrackMap()
    {
        double now = sectionClock.Elapsed.TotalSeconds;
        if (now < nextMapSample) return;
        nextMapSample = now + 0.05;
        if (mapsDirty && now >= nextMapSave)
        {
            SaveTrackMaps();
            nextMapSave = now + 10;
        }
        if (reloadInProgress || !FindGame()) return;
        var frame = ReadTelemetry(false);
        if (frame == null || !frame.HasCoordinates || frame.Speed < 10) return;
        var map = FindTrackMap(frame.Track);
        if (map == null)
        {
            map = new TrackMap { Track = frame.Track };
            trackMaps.Add(map);
        }
        if (map.Add(frame.Position, frame.X, frame.Z)) mapVersion++;
        mapsDirty = true;
    }
}
