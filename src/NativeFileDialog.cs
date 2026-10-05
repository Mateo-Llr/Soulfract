#nullable enable
using System.Runtime.InteropServices;
using System.Text;

namespace Soulfract;

internal static class NativeFileDialog
{
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_OVERWRITEPROMPT = 0x00000002;
    private const int OFN_NOCHANGEDIR = 0x00000008;

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileName(ref OpenFileName dialog);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetSaveFileName(ref OpenFileName dialog);

    public static string? OpenFile(string title)
    {
        return ShowDialog(title, "", OFN_PATHMUSTEXIST | OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR, false);
    }

    public static string? SaveFile(string title, string defaultFileName)
    {
        return ShowDialog(title, defaultFileName, OFN_PATHMUSTEXIST | OFN_OVERWRITEPROMPT | OFN_NOCHANGEDIR, true);
    }

    private static string? ShowDialog(string title, string defaultFileName, int flags, bool saveDialog)
    {
        IntPtr filter = Marshal.StringToHGlobalUni("Sauvegarde Soulfract (*.soulfract)\0*.soulfract\0Tous les fichiers (*.*)\0*.*\0\0");
        IntPtr titlePointer = Marshal.StringToHGlobalUni(title);
        IntPtr defaultExtension = Marshal.StringToHGlobalUni("soulfract");
        IntPtr file = Marshal.AllocHGlobal(260 * 2);
        try
        {
            for (int i = 0; i < 260 * 2; i++) Marshal.WriteByte(file, i, 0);
            if (!string.IsNullOrEmpty(defaultFileName))
                Marshal.Copy((defaultFileName + "\0").ToCharArray(), 0, file, defaultFileName.Length + 1);

            var dialog = new OpenFileName
            {
                StructSize = Marshal.SizeOf<OpenFileName>(),
                Filter = filter,
                Title = titlePointer,
                File = file,
                MaxFile = 260,
                DefExt = defaultExtension,
                Flags = flags
            };

            bool accepted = saveDialog ? GetSaveFileName(ref dialog) : GetOpenFileName(ref dialog);
            return accepted ? Marshal.PtrToStringUni(file) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(filter);
            Marshal.FreeHGlobal(titlePointer);
            Marshal.FreeHGlobal(defaultExtension);
            Marshal.FreeHGlobal(file);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int StructSize;
        public IntPtr Owner;
        public IntPtr Instance;
        public IntPtr Filter;
        public IntPtr CustomFilter;
        public int MaxCustFilter;
        public int FilterIndex;
        public IntPtr File;
        public int MaxFile;
        public IntPtr FileTitle;
        public int MaxFileTitle;
        public IntPtr InitialDir;
        public IntPtr Title;
        public int Flags;
        public short FileOffset;
        public short FileExtension;
        public IntPtr DefExt;
        public IntPtr CustData;
        public IntPtr Hook;
        public IntPtr TemplateName;
        public IntPtr ReservedPtr;
        public int ReservedInt;
        public int FlagsEx;
    }
}