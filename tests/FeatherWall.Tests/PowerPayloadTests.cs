using System.Runtime.InteropServices;
using FeatherWall.Interop;
using Xunit;

namespace FeatherWall.Tests;

/// <summary>What FeatherWall accepts as a WM_POWERBROADCAST payload.
///
/// The message number is below WM_USER, so Windows will not marshal its lParam across a process
/// boundary — any local process at this integrity level can post one carrying an arbitrary
/// pointer. Dereferencing that takes the process down with an access violation no catch block can
/// intercept, so the screen runs before the only dereference on the path. Not a privilege
/// boundary — a same-integrity caller can terminate FeatherWall anyway — but a stray message
/// should not be able to end the wallpaper.</summary>
public class PowerPayloadTests
{
    private static IntPtr Allocate(Guid setting, uint dataLength, byte data)
    {
        var payload = new POWERBROADCAST_SETTING { PowerSetting = setting, DataLength = dataLength, Data = data };
        IntPtr block = Marshal.AllocHGlobal(Marshal.SizeOf<POWERBROADCAST_SETTING>());
        Marshal.StructureToPtr(payload, block, false);
        return block;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr address, nuint size, uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(IntPtr address, nuint size, uint newProtect, out uint oldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFree(IntPtr address, nuint size, uint freeType);

    private const uint MEM_COMMIT_RESERVE = 0x1000 | 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint PAGE_EXECUTE = 0x10;
    private const uint PAGE_NOACCESS = 0x01;

    [Theory]
    [InlineData(PAGE_EXECUTE)]  // grants execute and NOT read
    [InlineData(PAGE_NOACCESS)]
    public void APageWithoutReadAccess_IsRejected(uint protection)
    {
        // Written while the page is still writable, then locked down — the payload is perfectly
        // well-formed, so the only thing that can reject it is the protection check.
        nuint size = (nuint)Marshal.SizeOf<POWERBROADCAST_SETTING>();
        IntPtr page = VirtualAlloc(IntPtr.Zero, size, MEM_COMMIT_RESERVE, PAGE_READWRITE);
        Assert.NotEqual(IntPtr.Zero, page);
        try
        {
            Marshal.StructureToPtr(
                new POWERBROADCAST_SETTING
                {
                    PowerSetting = PowerNotifications.ConsoleDisplayState,
                    DataLength = sizeof(uint),
                    Data = PowerNotifications.DisplayOn,
                }, page, false);

            Assert.True(VirtualProtect(page, size, protection, out _));

            Assert.False(PowerNotifications.TryRead(page, out _, out _));
        }
        finally { VirtualFree(page, 0, MEM_RELEASE); }
    }

    [Fact]
    public void BothDisplayStateSettings_AreRoutedAsDisplayState()
    {
        // Both are registered for. Routing only the console one subscribed to the session signal
        // and then dropped it — and the session one is what carries the answer over RDP.
        Assert.True(PowerNotifications.IsDisplayState(PowerNotifications.ConsoleDisplayState));
        Assert.True(PowerNotifications.IsDisplayState(PowerNotifications.SessionDisplayStatus));
    }

    [Fact]
    public void ThePowerSourceSettings_AreNotDisplayState()
    {
        Assert.False(PowerNotifications.IsDisplayState(PowerNotifications.AcDcPowerSource));
        Assert.False(PowerNotifications.IsDisplayState(PowerNotifications.PowerSavingStatus));
        Assert.False(PowerNotifications.IsDisplayState(Guid.Empty));
    }

    [Fact]
    public void NullPointer_IsRejected()
    {
        Assert.False(PowerNotifications.TryRead(IntPtr.Zero, out _, out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0x1000)]
    [InlineData(-1)]
    public void APointerIntoNothing_IsRejectedRatherThanDereferenced(long address)
    {
        // Before the VirtualQuery screen this crashed the process rather than returning false.
        Assert.False(PowerNotifications.TryRead(new IntPtr(address), out _, out _));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(3u)]
    public void APayloadNarrowerThanADword_IsRejected(uint dataLength)
    {
        // Every setting registered here carries a DWORD. A shorter one used to be accepted and its
        // first byte returned, which invents a display state out of a truncated payload — and
        // DisplayOff is 0, so the invented answer was "the screen is off".
        IntPtr block = Allocate(PowerNotifications.ConsoleDisplayState, dataLength, PowerNotifications.DisplayOn);
        try
        {
            Assert.False(PowerNotifications.TryRead(block, out _, out _));
        }
        finally { Marshal.FreeHGlobal(block); }
    }

    [Theory]
    [InlineData(PowerNotifications.DisplayOff)]
    [InlineData(PowerNotifications.DisplayOn)]
    [InlineData(PowerNotifications.DisplayDimmed)]
    public void AWellFormedDwordPayload_IsRead(byte state)
    {
        IntPtr block = Allocate(PowerNotifications.ConsoleDisplayState, sizeof(uint), state);
        try
        {
            Assert.True(PowerNotifications.TryRead(block, out var setting, out byte value));
            Assert.Equal(PowerNotifications.ConsoleDisplayState, setting);
            Assert.Equal(state, value);
        }
        finally { Marshal.FreeHGlobal(block); }
    }
}
