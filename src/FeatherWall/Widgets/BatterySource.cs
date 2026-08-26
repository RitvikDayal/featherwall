using FeatherWall.Interop;

namespace FeatherWall.Widgets;

/// <summary>What the power status says about the battery, as a state rather than a sentence.
/// None covers both "this machine has no battery" and "Windows will not say what the level is" —
/// in both cases there is nothing honest to draw or write.</summary>
public enum BatteryState { None, Charging, Charged, OnBattery, UnknownSource }

public readonly record struct BatteryReading(int Percent, BatteryState State);

/// <summary>Battery state as a line of text. Reads the SYSTEM_POWER_STATUS already P/Invoked
/// for battery-saver detection — no new interop, and no timer: Windows pushes a power-setting
/// notification on every percentage change.</summary>
public sealed partial class BatterySource : IWidgetSource
{
    private const byte NoSystemBattery = 128;
    private const byte UnknownPercent = 255;
    private const byte OnAcPower = 1;
    private const byte UnknownAcStatus = 255;

    /// <summary>The decision table, once. Pure, so the whole of it is testable without a battery.
    ///
    /// ACLineStatus 255 is "Windows cannot tell". The charge is still known, so it is reported —
    /// but neither charging nor on-battery is something this can honestly claim, and treating
    /// unknown as not-on-AC would state the latter as though it were a reading.</summary>
    public static BatteryReading Read(in SYSTEM_POWER_STATUS status)
    {
        if ((status.BatteryFlag & NoSystemBattery) != 0) return new BatteryReading(0, BatteryState.None);
        if (status.BatteryLifePercent == UnknownPercent) return new BatteryReading(0, BatteryState.None);

        int percent = status.BatteryLifePercent;
        if (status.ACLineStatus == UnknownAcStatus) return new BatteryReading(percent, BatteryState.UnknownSource);
        if (status.ACLineStatus != OnAcPower) return new BatteryReading(percent, BatteryState.OnBattery);
        return new BatteryReading(percent, percent >= 100 ? BatteryState.Charged : BatteryState.Charging);
    }

    /// <summary>A rendering of <see cref="Read"/>. Two independent readings of the same struct
    /// would eventually disagree, and the halo and the words would describe different states.</summary>
    public static string? Format(in SYSTEM_POWER_STATUS status)
    {
        var reading = Read(status);
        return reading.State switch
        {
            BatteryState.None => null,
            BatteryState.Charged => "charged",
            BatteryState.Charging => $"{reading.Percent}% charging",
            BatteryState.OnBattery => $"{reading.Percent}% on battery",
            _ => $"{reading.Percent}%",
        };
    }

    private string? _value;
    private BatteryReading _current;

    public string? Value => _value;

    /// <summary>The structured reading behind Value, for the halo. Updated in the same place, so
    /// the ring and the text can never describe different states.</summary>
    public BatteryReading Current => _current;
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
        BatteryReading reading = default;
        string? next = null;
        if (Kernel32.GetSystemPowerStatus(out var status))
        {
            reading = Read(status);
            next = Format(status);
        }

        // Both, not just the text: the halo redraws on a level change that leaves the words alone,
        // which is every single percent while charging.
        if (next == _value && reading == _current) return;
        _value = next;
        _current = reading;
        Changed?.Invoke();
    }

    public void Dispose() { }
}
