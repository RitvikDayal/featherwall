using FeatherWall.Interop;

namespace FeatherWall.Widgets;

/// <summary>Battery state as a line of text. Reads the SYSTEM_POWER_STATUS already P/Invoked
/// for battery-saver detection — no new interop, and no timer: Windows pushes a power-setting
/// notification on every percentage change.</summary>
public sealed partial class BatterySource
{
    private const byte NoSystemBattery = 128;
    private const byte UnknownPercent = 255;
    private const byte OnAcPower = 1;

    /// <summary>Pure so the whole decision table is testable without a battery.</summary>
    public static string? Format(in SYSTEM_POWER_STATUS status)
    {
        if ((status.BatteryFlag & NoSystemBattery) != 0) return null;
        if (status.BatteryLifePercent == UnknownPercent) return null;

        bool onAc = status.ACLineStatus == OnAcPower;
        if (onAc && status.BatteryLifePercent >= 100) return "charged";
        return onAc
            ? $"{status.BatteryLifePercent}% charging"
            : $"{status.BatteryLifePercent}% on battery";
    }
}
