using FeatherWall.Interop;

namespace FeatherWall.Widgets;

/// <summary>Battery state as a line of text. Reads the SYSTEM_POWER_STATUS already P/Invoked
/// for battery-saver detection — no new interop, and no timer: Windows pushes a power-setting
/// notification on every percentage change.</summary>
public sealed partial class BatterySource : IWidgetSource
{
    private const byte NoSystemBattery = 128;
    private const byte UnknownPercent = 255;
    private const byte OnAcPower = 1;
    private const byte UnknownAcStatus = 255;

    /// <summary>Pure so the whole decision table is testable without a battery.</summary>
    public static string? Format(in SYSTEM_POWER_STATUS status)
    {
        if ((status.BatteryFlag & NoSystemBattery) != 0) return null;
        if (status.BatteryLifePercent == UnknownPercent) return null;

        // ACLineStatus 255 is "Windows cannot tell". The charge is still known, so it is shown —
        // but neither "charging" nor "on battery" is something this can honestly say, and
        // treating unknown as not-on-AC would print the latter as though it were a reading.
        if (status.ACLineStatus == UnknownAcStatus) return $"{status.BatteryLifePercent}%";

        bool onAc = status.ACLineStatus == OnAcPower;
        if (onAc && status.BatteryLifePercent >= 100) return "charged";
        return onAc
            ? $"{status.BatteryLifePercent}% charging"
            : $"{status.BatteryLifePercent}% on battery";
    }

    private string? _value;

    public string? Value => _value;
    public event Action? Changed;

    public BatterySource() => Refresh();

    /// <summary>Called from the engine's WM_POWERBROADCAST handler. Re-reads only for the two
    /// settings that can change the answer, so an unrelated power event costs one comparison.</summary>
    public void OnPowerSettingChanged(Guid setting)
    {
        if (setting != PowerNotifications.BatteryPercentageRemaining &&
            setting != PowerNotifications.AcDcPowerSource) return;
        Refresh();
    }

    /// <summary>Re-reads the power status. Called on resume from sleep: nothing pushes while the
    /// machine is off, so the percentage has moved and no notification will announce it.</summary>
    public void Refresh()
    {
        string? next = Kernel32.GetSystemPowerStatus(out var status) ? Format(status) : null;
        if (next == _value) return;   // no event when the text has not moved
        _value = next;
        Changed?.Invoke();
    }

    public void Dispose() { }
}
