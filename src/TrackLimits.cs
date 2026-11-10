using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;

internal static partial class RepeatCorner
{
    private static readonly Dictionary<string, TrackLimits> trackLimits =
        new Dictionary<string, TrackLimits>(StringComparer.OrdinalIgnoreCase);

    private static TrackLimits FindTrackLimits(string track)
    {
        if (string.IsNullOrEmpty(track) || track.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        TrackLimits limits;
        if (trackLimits.TryGetValue(track, out limits)) return limits;
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TrackLimits", track + ".csv");
        try
        {
            if (File.Exists(path))
            {
                limits = TrackLimits.Read(path);
                DiagnosticLog.Info("Experimental coordinate track limits enabled for " + track + ".");
            }
        }
        catch (IOException error) { DiagnosticLog.Error("Could not load track limits for " + track, error); }
        catch (InvalidDataException error) { DiagnosticLog.Error("Could not load track limits for " + track, error); }
        catch (UnauthorizedAccessException error) { DiagnosticLog.Error("Could not load track limits for " + track, error); }
        trackLimits.Add(track, limits);
        return limits;
    }

    internal sealed class TrackLimits
    {
        private readonly PointF[] left, right;

        internal TrackLimits(PointF[] left, PointF[] right)
        {
            this.left = left;
            this.right = right;
        }

        // Two closed boundary rings in ACC world metres, separated by a blank line.
        internal static TrackLimits Read(string path)
        {
            var rings = new[] { new List<PointF>(), new List<PointF>() };
            int ring = 0;
            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (rings[ring].Count > 0) ring = 1;
                    continue;
                }
                var columns = line.Split(',');
                float x, z;
                if (columns.Length != 2 ||
                    !float.TryParse(columns[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                    !float.TryParse(columns[1], NumberStyles.Float, CultureInfo.InvariantCulture, out z) ||
                    float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z))
                    throw new InvalidDataException("Expected an X,Z point in world metres.");
                rings[ring].Add(new PointF(x, z));
            }
            if (rings[0].Count < 3 || rings[1].Count < 3)
                throw new InvalidDataException("Expected two boundary rings.");
            return new TrackLimits(rings[0].ToArray(), rings[1].ToArray());
        }

        internal double OutsideMetres(double x, double z)
        {
            double nearestSquared = double.PositiveInfinity;
            bool insideLeft = Contains(left, x, z, ref nearestSquared);
            bool insideRight = Contains(right, x, z, ref nearestSquared);
            return insideLeft != insideRight ? 0 : Math.Sqrt(nearestSquared);
        }

        private static bool Contains(PointF[] ring, double x, double z, ref double nearestSquared)
        {
            bool inside = false;
            PointF a = ring[ring.Length - 1];
            foreach (PointF b in ring)
            {
                double dx = b.X - a.X, dz = b.Y - a.Y;
                double px = x - a.X, pz = z - a.Y;
                if ((a.Y > z) != (b.Y > z) && px < dx * pz / dz)
                    inside = !inside;
                double lengthSquared = dx * dx + dz * dz;
                if (lengthSquared > 0)
                {
                    double fraction = Math.Max(0, Math.Min(1, (px * dx + pz * dz) / lengthSquared));
                    double gapX = px - dx * fraction, gapZ = pz - dz * fraction;
                    nearestSquared = Math.Min(nearestSquared, gapX * gapX + gapZ * gapZ);
                }
                a = b;
            }
            return inside;
        }
    }

    internal sealed class BoundaryTrace
    {
        // The imported edges are approximate. Allow a small margin and require more
        // than a transient excursion before the pilot rejects a run.
        private const double MarginMetres = 0.25, MinimumSecondsOutside = 0.12;
        private double? outsideSince;
        internal double MaxOutsideMetres, CutMetres, CutX, CutZ;
        internal int Samples;

        internal void Observe(TrackLimits limits, double time, PointF[] before, PointF[] current, double fraction)
        {
            if (before == null || current == null)
            {
                outsideSince = null;
                return;
            }
            double outside = double.PositiveInfinity;
            double nearestX = 0, nearestZ = 0;
            for (int wheel = 0; wheel < current.Length; wheel++)
            {
                double x = before[wheel].X + (current[wheel].X - before[wheel].X) * fraction;
                double z = before[wheel].Y + (current[wheel].Y - before[wheel].Y) * fraction;
                double distance = limits.OutsideMetres(x, z);
                if (distance >= outside) continue;
                outside = distance;
                nearestX = x;
                nearestZ = z;
                if (outside == 0) break;
            }
            Samples++;
            MaxOutsideMetres = Math.Max(MaxOutsideMetres, outside);
            if (outside <= MarginMetres)
            {
                outsideSince = null;
                return;
            }
            if (!outsideSince.HasValue) outsideSince = time;
            if (time - outsideSince.Value < MinimumSecondsOutside || outside <= CutMetres) return;
            CutMetres = outside;
            CutX = nearestX;
            CutZ = nearestZ;
        }
    }
}
