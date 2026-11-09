using Xunit;
using static RepeatCorner;

namespace ACCRepeat.Tests
{
    // The hands-free loop and the track map cache are still static fields of the engine, so
    // the tests that touch them run one at a time. Everything else runs in parallel.
    [CollectionDefinition(Name)]
    public sealed class EngineStatics
    {
        public const string Name = "Engine statics";
    }

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
