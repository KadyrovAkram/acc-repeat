using System;

internal static class PracticeDrillTests
{
    private static int Main()
    {
        RepeatCorner.VerifyPracticeDrills();
        Console.WriteLine("Practice drill checks passed.");
        return 0;
    }
}

internal static partial class RepeatCorner
{
    internal static void VerifyPracticeDrills()
    {
        var oldSave = new Save { Row = 1, Slot = "4", Created = "old", Track = "monza", Mode = "Practice" };
        var newer = new Save { Row = 0, Slot = "5", Created = "new", Track = "monza", Mode = "Practice" };
        var pin = new Checkpoint { Name = "Monza T1", Slot = "4", Created = "old", Track = "monza" };
        Require(SelectSave(new[] { newer, oldSave }, pin) == oldSave, "A newer save displaced the pin.");
        Require(SelectSave(new[] { newer, oldSave }, null) == newer, "Latest mode did not select the first save.");
        oldSave.Created = "replacement";
        try
        {
            SelectSave(new[] { newer, oldSave }, pin);
            throw new Exception("A replaced save was accepted as the pinned save.");
        }
        catch (InvalidOperationException) { }

        var persisted = new ToolSettings { ActiveCheckpoint = pin.Name, Wheel = new WheelBinding { Button = 31 } };
        persisted.Checkpoints.Add(pin);
        var roundTrip = Json.Deserialize<ToolSettings>(Json.Serialize(persisted));
        Require(roundTrip.Checkpoints[0].Created == pin.Created && roundTrip.Wheel.Button == 31,
            "Pin or wheel binding failed to round-trip.");
        Require(ButtonIndex(1u << 31) == 31, "Button 32 was decoded incorrectly.");

        var timer = new SectionTimer();
        var section = new Section { Track = "monza", Start = 0.9995, Finish = 0.0005 };
        timer.Observe(section, Frame(1, 0.999, 0, 0, 100));
        timer.Observe(section, Frame(2, 0.000, 0.1, 3, 120));
        var result = timer.Observe(section, Frame(3, 0.001, 0.2, 6, 140));
        Require(result != null && Math.Abs(result.Seconds - 0.1) < 0.000001 &&
            Math.Abs(result.EntrySpeed - 110) < 0.000001 && Math.Abs(result.ExitSpeed - 130) < 0.000001,
            "Crossing interpolation or lap wrap is incorrect.");

        timer.Reset();
        var normal = new Section { Track = "monza", Start = 0.1, Finish = 0.102 };
        timer.Observe(normal, Frame(1, 0.099, 0, 0, 100));
        timer.Observe(normal, Frame(2, 0.101, 0.1, 3, 105));
        Require(timer.Observe(normal, Frame(3, 0.103, 0.2, 200, 120)) == null,
            "A teleport across the finish counted as an attempt.");

        timer.Reset();
        timer.Observe(normal, Frame(1, 0.099, 0, 0, 100));
        timer.Observe(normal, Frame(2, 0.101, 0.1, 3, 105));
        timer.Observe(normal, null);
        timer.Observe(normal, Frame(3, 0.101, 0.2, 3, 105));
        Require(timer.Observe(normal, Frame(4, 0.103, 0.3, 6, 120)) == null,
            "An interrupted run counted after resuming mid-section.");
    }

    private static TelemetryFrame Frame(int packet, double position, double time, double distance, double speed)
    {
        return new TelemetryFrame
        {
            Track = "monza", Packet = packet, Position = position, Time = time, Distance = distance, Speed = speed
        };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
