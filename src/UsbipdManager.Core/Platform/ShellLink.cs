using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace UsbipdManager.Core.Platform;

/// <summary>Minimal wrapper over the shell's ShellLink COM object for writing and reading .lnk files.</summary>
internal static class ShellLink
{
    private static readonly Guid ShellLinkClsid = new("00021401-0000-0000-C000-000000000046");

    private const int MaxPath = 32767;

    public static void Create(string linkPath, string targetPath, string workingDirectory, string description, string iconPath, int iconIndex)
    {
        var link = CreateInstance();
        try
        {
            link.SetPath(targetPath);
            link.SetWorkingDirectory(workingDirectory);
            link.SetDescription(description);
            link.SetIconLocation(iconPath, iconIndex);
            ((IPersistFile)link).Save(linkPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    public static ShellLinkInfo Read(string linkPath)
    {
        var link = CreateInstance();
        try
        {
            ((IPersistFile)link).Load(linkPath, 0);

            var target = new StringBuilder(MaxPath);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
            var workingDirectory = new StringBuilder(MaxPath);
            link.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);
            var description = new StringBuilder(1024);
            link.GetDescription(description, description.Capacity);
            var icon = new StringBuilder(MaxPath);
            link.GetIconLocation(icon, icon.Capacity, out var iconIndex);

            return new ShellLinkInfo(target.ToString(), workingDirectory.ToString(), description.ToString(), icon.ToString(), iconIndex);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    private static IShellLinkW CreateInstance()
    {
        var type = Type.GetTypeFromCLSID(ShellLinkClsid, throwOnError: true)!;
        return (IShellLinkW)Activator.CreateInstance(type)!;
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}

internal sealed record ShellLinkInfo(string TargetPath, string WorkingDirectory, string Description, string IconPath, int IconIndex);
