using System.Runtime.InteropServices;
using FeatherWall.Interop;

namespace FeatherWall.Tray;

public static class FilePicker
{
    private const string Filter =
        "Images & videos\0*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.mp4;*.m4v;*.mov;*.avi;*.wmv;*.mkv;*.webm\0" +
        "Images\0*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff\0" +
        "Videos\0*.mp4;*.m4v;*.mov;*.avi;*.wmv;*.mkv;*.webm\0" +
        "All files\0*.*\0\0";

    public static string? PickMedia(IntPtr owner)
    {
        const int bufferChars = 4096;
        IntPtr buffer = Marshal.AllocHGlobal(bufferChars * 2);
        try
        {
            // zero the buffer so the dialog sees an empty initial filename
            for (int i = 0; i < bufferChars; i++) Marshal.WriteInt16(buffer, i * 2, 0);

            var ofn = new OPENFILENAME
            {
                StructSize = (uint)Marshal.SizeOf<OPENFILENAME>(),
                HwndOwner = owner,
                Filter = Filter,
                File = buffer,
                MaxFile = bufferChars,
                Title = "Choose a wallpaper (image or video)",
                Flags = ComDlg32.OFN_FILEMUSTEXIST | ComDlg32.OFN_PATHMUSTEXIST | ComDlg32.OFN_NOCHANGEDIR,
            };
            return ComDlg32.GetOpenFileNameW(ref ofn) ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
