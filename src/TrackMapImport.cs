using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static partial class RepeatCorner
{
    private struct ImportedMapPoint
    {
        public double X, Z, Position;
    }

    private static TrackMapView ImportTrackMap(string track, string path)
    {
        var map = ReadTrackMapFile(track, path);
        var updated = trackMaps.Where(old => !old.Imported || !SameTrack(old.Track, track)).ToList();
        updated.Add(map);
        WriteTrackMaps(updated);
        trackMaps = updated;
        mapsDirty = false;
        mapVersion++;
        return MapView(track);
    }

    private static TrackMap ReadTrackMapFile(string track, string path)
    {
        // The binary format has no track ID in its header. Use the original filename.
        if (!SameTrack(Path.GetFileName(path), track + ".bin"))
            throw new InvalidDataException("Choose the " + track + ".bin file for this checkpoint.");

        const int headerSize = 14, pointSize = 12, maxFileSize = 4 * 1024 * 1024;
        using (var stream = File.OpenRead(path))
        using (var reader = new BinaryReader(stream))
        {
            long payload = stream.Length - headerSize;
            if (payload < 2 * pointSize || payload % pointSize != 0 || stream.Length > maxFileSize ||
                reader.ReadUInt32() != 0x6d746572 || reader.ReadUInt16() != 1)
                throw new InvalidDataException("Choose a Race Element ACC track map in the supported version 1 format.");

            stream.Position = headerSize;
            int count = (int)(payload / pointSize);
            var points = new List<ImportedMapPoint>(count);
            for (int i = 0; i < count; i++)
            {
                float x = reader.ReadSingle(), z = reader.ReadSingle(), position = reader.ReadSingle();
                if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z) ||
                    float.IsNaN(position) || position < 0 || position > 1)
                    throw new InvalidDataException("The map contains invalid coordinates or lap positions.");
                points.Add(new ImportedMapPoint { X = x, Z = z, Position = position });
            }
            return SampleImportedTrackMap(track, points);
        }
    }

    private static TrackMap SampleImportedTrackMap(string track, List<ImportedMapPoint> points)
    {
        // Sort by ACC lap position, then bracket the lap seam with copies of the endpoints.
        var ordered = points.OrderBy(point => point.Position).ToList();
        var beforeStart = ordered[ordered.Count - 1];
        var afterFinish = ordered[0];
        beforeStart.Position -= 1;
        afterFinish.Position += 1;
        ordered.Insert(0, beforeStart);
        ordered.Add(afterFinish);

        const double maxLapGap = 0.005;
        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Position - ordered[i - 1].Position > maxLapGap)
                throw new InvalidDataException("The map is missing part of the lap. Choose a complete track map.");
        }

        var map = new TrackMap { Track = track, Imported = true };
        int next = 1;
        for (int bin = 0; bin < MapBins; bin++)
        {
            double position = (bin + 0.5) / MapBins;
            while (ordered[next].Position < position) next++;
            var a = ordered[next - 1];
            var b = ordered[next];
            double fraction = (position - a.Position) / (b.Position - a.Position);
            map.X[bin] = a.X + (b.X - a.X) * fraction;
            map.Z[bin] = a.Z + (b.Z - a.Z) * fraction;
            map.Filled[bin] = true;
        }
        return map;
    }
}
