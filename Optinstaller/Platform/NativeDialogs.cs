using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Optinstaller.Platform;

public static class NativeDialogs
{
    private const int MaxPath = 260;
    private const int MaxFileBuffer = 4096;

    public static string? PickFolder(string title, nint owner = 0)
    {
        var browseInfo = new BrowseInfo
        {
            Owner = owner,
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

    public static string? PickFile(string title, string filter, nint owner = 0)
    {
        var filterPointer = IntPtr.Zero;
        var titlePointer = IntPtr.Zero;
        var fileBufferPointer = IntPtr.Zero;

        try
        {
            filterPointer = Marshal.StringToHGlobalUni(NormalizeFilter(filter));
            titlePointer = string.IsNullOrWhiteSpace(title) ? IntPtr.Zero : Marshal.StringToHGlobalUni(title);
            fileBufferPointer = Marshal.AllocHGlobal(MaxFileBuffer * sizeof(char));
            Marshal.WriteInt16(fileBufferPointer, 0);

            var openFileName = new OpenFileName
            {
                StructSize = Marshal.SizeOf<OpenFileName>(),
                Owner = owner,
                Filter = filterPointer,
                File = fileBufferPointer,
                MaxFile = MaxFileBuffer,
                Title = titlePointer,
                Flags = OpenFileNameFlags.PathMustExist | OpenFileNameFlags.FileMustExist | OpenFileNameFlags.NoChangeDir,
            };

            if (!GetOpenFileName(ref openFileName))
            {
                return null;
            }

            return Marshal.PtrToStringUni(fileBufferPointer);
        }
        finally
        {
            if (filterPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(filterPointer);
            }

            if (titlePointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(titlePointer);
            }

            if (fileBufferPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(fileBufferPointer);
            }
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

    [Flags]
    private enum OpenFileNameFlags : uint
    {
        FileMustExist = 0x00001000,
        PathMustExist = 0x00000800,
        NoChangeDir = 0x00000008,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int StructSize;
        public IntPtr Owner;
        public IntPtr Instance;

        public IntPtr Filter;

        public IntPtr CustomFilter;

        public int MaxCustomFilter;
        public int FilterIndex;
        public IntPtr File;
        public int MaxFile;

        public IntPtr FileTitle;

        public int MaxFileTitle;

        public IntPtr InitialDir;

        public IntPtr Title;

        public OpenFileNameFlags Flags;
        public short FileOffset;
        public short FileExtension;

        public IntPtr DefaultExtension;

        public IntPtr CustomData;
        public IntPtr Hook;

        public IntPtr TemplateName;

        public IntPtr Reserved;
        public int ReservedInt;
        public int FlagsEx;
    }

    private static string NormalizeFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return "All Files (*.*)\0*.*\0\0";
        }

        return filter.Replace('|', '\0').TrimEnd('\0') + "\0\0";
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolder(ref BrowseInfo browseInfo);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileName openFileName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder path);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pointer);
}
