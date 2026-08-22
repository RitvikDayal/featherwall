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
