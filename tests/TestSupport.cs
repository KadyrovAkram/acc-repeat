using Xunit;
using static RepeatCorner;

// The app keeps its settings, loop state and track maps in static fields, so tests that
// touch them must not run at the same time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ACCRepeat.Tests
{
    internal static class Telemetry
    {
        public static TelemetryFrame Frame(int packet, double position, double time, double distance, double speed)
        {
            return new TelemetryFrame
            {
                Track = "monza", Packet = packet, Position = position, Time = time, Distance = distance, Speed = speed
            };
        }
    }
}
