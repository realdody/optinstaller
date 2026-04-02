using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Optinstaller.Platform;

public static class NativeDialogs
{
    private const int MaxPath = 260;

    public static string? PickFolder(string title)
    {
        var browseInfo = new BrowseInfo
        {
            Title = title,
            Flags = BrowseInfoFlags.ReturnOnlyFsDirs | BrowseInfoFlags.UseNewUi | BrowseInfoFlags.NoNewFolderButton,
        };

        var pidl = SHBrowseForFolder(ref browseInfo);
        if (pidl == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var path = new StringBuilder(MaxPath);
            return SHGetPathFromIDList(pidl, path) ? path.ToString() : null;
        }
        finally
        {
            CoTaskMemFree(pidl);
        }
    }

    [Flags]
    private enum BrowseInfoFlags : uint
    {
        ReturnOnlyFsDirs = 0x0001,
        EditBox = 0x0010,
        NewDialogStyle = 0x0040,
        UseNewUi = NewDialogStyle | EditBox,
        NoNewFolderButton = 0x0200,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BrowseInfo
    {
        public IntPtr Owner;

        public IntPtr Root;

        public IntPtr DisplayName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Title;

        public BrowseInfoFlags Flags;

        public IntPtr Callback;

        public IntPtr Parameter;

        public int Image;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolder(ref BrowseInfo browseInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder path);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pointer);
}
