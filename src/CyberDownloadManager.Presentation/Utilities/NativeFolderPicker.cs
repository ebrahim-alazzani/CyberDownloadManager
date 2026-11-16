using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CyberDownloadManager.Presentation.Utilities;

/// <summary>
/// Opens the Windows Shell folder browser without relying on the WinRT picker,
/// which is unavailable to elevated desktop processes on older Windows App SDKs.
/// </summary>
internal static class NativeFolderPicker
{
    private const uint BrowseForFilesystemDirectories = 0x0001;
    private const uint BrowseWithEditBox = 0x0010;
    private const uint BrowseNewDialogStyle = 0x0040;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BrowseInfo
    {
        public IntPtr Owner;
        public IntPtr Root;
        public IntPtr DisplayName;
        [MarshalAs(UnmanagedType.LPWStr)] public string Title;
        public uint Flags;
        public IntPtr Callback;
        public IntPtr Parameter;
        public int Image;
    }

    [DllImport("shell32.dll", EntryPoint = "SHBrowseForFolderW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BrowseForFolder(ref BrowseInfo info);

    [DllImport("shell32.dll", EntryPoint = "SHGetPathFromIDListEx", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPathFromIdList(IntPtr itemIdList, StringBuilder path, uint pathLength, uint flags);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr memory);

    public static string? PickFolder(IntPtr owner, string title)
    {
        IntPtr displayName = Marshal.AllocHGlobal(1024 * sizeof(char));
        try
        {
            var info = new BrowseInfo
            {
                Owner = owner,
                DisplayName = displayName,
                Title = title,
                Flags = BrowseForFilesystemDirectories | BrowseWithEditBox | BrowseNewDialogStyle
            };

            var itemIdList = BrowseForFolder(ref info);
            if (itemIdList == IntPtr.Zero)
                return null;

            try
            {
                var path = new StringBuilder(32768);
                if (!GetPathFromIdList(itemIdList, path, (uint)path.Capacity, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not read the selected folder path.");
                return path.ToString();
            }
            finally
            {
                CoTaskMemFree(itemIdList);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(displayName);
        }
    }
}
