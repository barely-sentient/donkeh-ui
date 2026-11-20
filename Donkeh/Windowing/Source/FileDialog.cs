using System.Runtime.InteropServices;
using Donkeh.UILab;

namespace Donkeh.Windowing;

/// <summary>
/// Shows the platform file-open chooser for an <c>input[type=file]</c>.
/// </summary>
/// <remarks>
/// Desktop hosts use the native Windows common file dialog. Other platforms (and
/// hosts without a window handle) return <see langword="false"/> so the caller
/// leaves the control untouched. The dialog is modal, so the frame loop pauses
/// while it is open.
/// </remarks>
internal static class FileDialog
{
    private const int OfnReadOnly = 0x00000001;
    private const int OfnNoChangeDir = 0x00000008;
    private const int OfnPathMustExist = 0x00000800;
    private const int OfnFileMustExist = 0x00001000;
    private const int OfnExplorer = 0x00080000;

    /// <summary>Shows the open-file chooser.</summary>
    /// <param name="owner">Native owner window handle, or zero for no owner.</param>
    /// <param name="path">Receives the selected absolute path on success.</param>
    /// <returns><see langword="true"/> when the user accepted a file.</returns>
    public static bool TryPickFile(IntPtr owner, out string path)
    {
        path = string.Empty;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        const int fileChars = 1024;
        const int titleChars = 512;
        var filter = Marshal.StringToHGlobalUni("All files\0*.*\0\0");
        var title = Marshal.StringToHGlobalUni("Open File");
        var file = Marshal.AllocHGlobal(fileChars * sizeof(char));
        var fileTitle = Marshal.AllocHGlobal(titleChars * sizeof(char));

        try
        {
            Marshal.Copy(new byte[fileChars * sizeof(char)], 0, file, fileChars * sizeof(char));
            Marshal.Copy(new byte[titleChars * sizeof(char)], 0, fileTitle, titleChars * sizeof(char));

            var data = new OpenFileName
            {
                Size = Marshal.SizeOf<OpenFileName>(),
                Owner = owner,
                Filter = filter,
                FilterIndex = 1,
                File = file,
                MaxFile = fileChars,
                FileTitle = fileTitle,
                MaxFileTitle = titleChars,
                Title = title,
                Flags = OfnExplorer
                    | OfnFileMustExist
                    | OfnPathMustExist
                    | OfnNoChangeDir
                    | OfnReadOnly
            };

            if (!GetOpenFileName(ref data))
            {
                return false;
            }

            path = Marshal.PtrToStringUni(file) ?? string.Empty;
            return !string.IsNullOrEmpty(path);
        }
        finally
        {
            Marshal.FreeHGlobal(filter);
            Marshal.FreeHGlobal(title);
            Marshal.FreeHGlobal(file);
            Marshal.FreeHGlobal(fileTitle);
        }
    }

    /// <summary>Stores a selected path on a file input.</summary>
    /// <param name="element">The <c>input[type=file]</c> receiving the value.</param>
    /// <param name="path">Absolute path selected in the chooser.</param>
    /// <returns>The file name shown by the control.</returns>
    public static string Apply(ElementNode element, string path)
    {
        element.SetAttribute("value", path);
        return Path.GetFileName(path);
    }

    /// <summary>Win32 <c>OPENFILENAMEW</c>.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int Size;
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
        public IntPtr InitialDirectory;
        public IntPtr Title;
        public int Flags;
        public short FileOffset;
        public short FileExtension;
        public IntPtr DefaultExtension;
        public IntPtr CustomData;
        public IntPtr Hook;
        public IntPtr TemplateName;
        public IntPtr Reserved;
        public int Reserved2;
        public int FlagsEx;
    }

    [DllImport(
        "comdlg32.dll",
        EntryPoint = "GetOpenFileNameW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileName data);
}
