using System.Runtime.InteropServices;

namespace PrismOS.Interop;

/// <summary>WASAPI capture interfaces for a system-audio loopback. The same approach
/// Windhawk's taskbar media player and the Windows "sound meter" use: the default
/// render endpoint's loopback captures everything the user hears, including their
/// music, in real time.
///
/// Raw COM interop rather than a package: the init is one endpoint activation and
/// a handful of calls, and the codebase keeps its interop in this style.</summary>
internal static class AudioInterop
{
    // Audio device roles / flows
    internal const int eRender = 0;
    internal const int eConsole = 0;

    // Audio share mode
    internal const int AudioClientShareModeShared = 0;

    // Stream flags
    internal const uint StreamFlagsLoopback = 0x00020000;
    internal const uint StreamFlagsAutoConvertPcm = 0x80000000;
    internal const uint StreamFlagsEventCallback = 0x00040000;

    // Buffer flags
    internal const uint BufferFlagsSilent = 0x00000002;

    // WAVEFORMATEX.FormatTag for IEEE float
    internal const ushort WaveFormatIeeeFloat = 3;

    internal static readonly Guid IIDAudioClient = new("1cb9a9ac-8dda-4ddb-84f2-04a3fb47a83a");
    internal static readonly Guid IIDCaptureClient = new("c8cddbfa-66a1-4c9a-b888-0bc26c60d8d0");

    [ComImport, Guid("bcde0395-e52f-467c-8d64-c46c88630f02")]
    internal class MMDeviceEnumerator { }

    [ComImport, Guid("a95664d5-b03a-4c87-8f0b-f0a0db29d185"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int dwStateMask, out IMMDeviceCollection ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
    }

    [ComImport, Guid("0ca2c26a-45f8-11d2-8f07-006097de6400"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint pcDevices);
        [PreserveSig] int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [ComImport, Guid("d666ce8e-f43a-4eb7-8de5-26e70cf45fdb"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out object ppdb);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out int pdwState);
    }

    [ComImport, Guid("8e14a747-e679-4f5f-a39f-ed671fdfef09"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long hnsBufferDuration,
            long hnsPeriodicity, ref WAVEFORMATEX pFormat, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint pNumBufferFrames);
        [PreserveSig] int GetStreamLatency(out long phnsLatency);
        [PreserveSig] int GetCurrentPadding(out uint pNumPaddingFrames);
        [PreserveSig] int IsFormatSupported(int shareMode, ref WAVEFORMATEX pFormat, IntPtr phkFormatUsed);
        [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);
        [PreserveSig] int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }

    [ComImport, Guid("c8cddbfa-66a1-4c9a-b888-0bc26c60d8d0"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioCaptureClient
    {
        [PreserveSig] int GetBufferSize(out uint pNumBufferFrames);
        [PreserveSig] int GetNextPacketSize(out uint pNumFramesInNextPacket);
        [PreserveSig] int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead, out uint pdwFlags,
            out ulong pu64DevicePosition, out ulong pu64QPCPosition);
        [PreserveSig] int ReleaseBuffer(uint numFramesRead);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct WAVEFORMATEX
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;

        public static WAVEFORMATEX Float48kHzStereo() => new()
        {
            FormatTag = WaveFormatIeeeFloat,
            Channels = 2,
            SamplesPerSec = 48000,
            AvgBytesPerSec = 48000 * 2 * 4,
            BlockAlign = 2 * 4,
            BitsPerSample = 32,
            Size = 0,
        };
    }
}
