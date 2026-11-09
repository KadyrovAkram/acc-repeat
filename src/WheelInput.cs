using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static partial class RepeatCorner
{
    private const int WheelSlots = 16;
    private static readonly uint?[] previousButtons = new uint?[WheelSlots];
    private static Stopwatch learnWheel;
    private static bool wheelArmed;

    public sealed class WheelBinding
    {
        public int Slot, Button;
        public string Device;
    }

    private static void ConfigureWheel(string action)
    {
        if (action.Equals("bind", StringComparison.OrdinalIgnoreCase))
        {
            bool found = false;
            for (int slot = 0; slot < WheelSlots; slot++)
            {
                previousButtons[slot] = ReadButtons(slot);
                found |= previousButtons[slot].HasValue;
            }
            if (!found)
                throw new InvalidOperationException("Windows did not report a joystick or wheel. Connect it and try again.");
            learnWheel = Stopwatch.StartNew();
            ReportStatus("Release any held buttons, then press your Reset button within 15 seconds.");
        }
        else if (action.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            settings.Wheel = null;
            StoreSettings();
            learnWheel = null;
            wheelArmed = false;
            ReportStatus("Wheel binding cleared.");
        }
        else if (action.Length == 0)
        {
            var binding = settings.Wheel;
            Console.WriteLine(binding == null ? "Wheel is unbound. Type 'wheel bind'." :
                string.Format("Wheel: {0}, device slot {1}, button {2}.", binding.Device, binding.Slot, binding.Button + 1));
        }
        else
            throw new InvalidOperationException("Use 'wheel', 'wheel bind', or 'wheel clear'.");
    }

    private static void PollWheel()
    {
        if (learnWheel != null)
        {
            LearnWheelButton();
            return;
        }
        var binding = settings.Wheel;
        if (binding == null || binding.Slot < 0 || binding.Slot >= WheelSlots || binding.Button < 0 || binding.Button >= 32)
            return;
        uint? buttons = ReadButtons(binding.Slot);
        bool pressed = buttons.HasValue && (buttons.Value & (1u << binding.Button)) != 0;
        if (pressed && wheelArmed && binding.Device == ReadDevice(binding.Slot))
            TryRetry(false);
        wheelArmed = buttons.HasValue && !pressed;
    }

    private static void LearnWheelButton()
    {
        if (learnWheel.Elapsed.TotalSeconds >= 15)
        {
            learnWheel = null;
            ReportStatus("No new button press detected. Try binding the wheel again.");
            return;
        }
        for (int slot = 0; slot < WheelSlots; slot++)
        {
            uint? buttons = ReadButtons(slot);
            uint rising = buttons.GetValueOrDefault() & ~previousButtons[slot].GetValueOrDefault();
            bool connected = previousButtons[slot].HasValue;
            previousButtons[slot] = buttons;
            if (!connected || rising == 0 || (rising & (rising - 1)) != 0)
                continue;
            string device = ReadDevice(slot);
            if (device == null) continue;

            var oldBinding = settings.Wheel;
            var newBinding = new WheelBinding { Slot = slot, Button = ButtonIndex(rising), Device = device };
            settings.Wheel = newBinding;
            try
            {
                StoreSettings();
                ReportStatus(string.Format("Reset bound to {0}, button {1}.", device, newBinding.Button + 1));
            }
            catch (Exception error)
            {
                settings.Wheel = oldBinding;
                ReportStatus("Could not store the wheel binding. " + error.Message);
            }
            learnWheel = null;
            wheelArmed = false;
            return;
        }
    }

    private static int ButtonIndex(uint mask)
    {
        int index = 0;
        while ((mask >>= 1) != 0) index++;
        return index;
    }

    private static uint? ReadButtons(int slot)
    {
        var state = new JoystickState { Size = (uint)Marshal.SizeOf(typeof(JoystickState)), Flags = 0x80 };
        return joyGetPosEx((uint)slot, ref state) == 0 ? (uint?)state.Buttons : null;
    }

    private static string ReadDevice(int slot)
    {
        JoystickCaps caps;
        if (joyGetDevCapsW((UIntPtr)(uint)slot, out caps, (uint)Marshal.SizeOf(typeof(JoystickCaps))) != 0)
            return null;
        return string.Format("{0} ({1:X4}:{2:X4})", caps.Name, caps.Manufacturer, caps.Product);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JoystickState
    {
        public uint Size, Flags, X, Y, Z, R, U, V, Buttons, ButtonNumber, Pov, Reserved1, Reserved2;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JoystickCaps
    {
        public ushort Manufacturer, Product;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public uint XMin, XMax, YMin, YMax, ZMin, ZMax, NumButtons, PeriodMin, PeriodMax;
        public uint RMin, RMax, UMin, UMax, VMin, VMax, Caps, MaxAxes, NumAxes, MaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string RegistryKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Driver;
    }

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint joyGetPosEx(uint id, ref JoystickState state);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint joyGetDevCapsW(UIntPtr id, out JoystickCaps caps, uint size);
}
