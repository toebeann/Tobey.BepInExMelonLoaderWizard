//using System.ComponentModel;
//using System.Diagnostics;
//using System.Runtime.InteropServices;

//namespace Tobey.BepInExMelonLoaderWizard.MLDisabler;

//[StructLayout(LayoutKind.Sequential)]
//public struct ProcessHelper
//{
//    // These members must match PROCESS_BASIC_INFORMATION
//    internal IntPtr Reserved1;
//    internal IntPtr PebBaseAddress;
//    internal IntPtr Reserved2_0;
//    internal IntPtr Reserved2_1;
//    internal IntPtr UniqueProcessId;
//    internal IntPtr InheritedFromUniqueProcessId;

//    [DllImport("ntdll.dll")]
//    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref ProcessHelper processInformation, int processInformationLength, out int returnLength);

//    /// <summary>
//    /// Gets the parent process of a specified process.
//    /// </summary>
//    /// <param name="process">The process.</param>
//    /// <returns>An instance of the Process class.</returns>
//    public static Process? GetParentProcess(Process process) => GetParentProcess(process.Handle);

//    /// <summary>
//    /// Gets the parent process of a specified process.
//    /// </summary>
//    /// <param name="handle">The process handle.</param>
//    /// <returns>An instance of the Process class.</returns>
//    public static Process? GetParentProcess(IntPtr handle)
//    {
//        ProcessHelper pbi = new();
//        int status = NtQueryInformationProcess(handle, 0, ref pbi, Marshal.SizeOf(pbi), out int _);
//        if (status != 0)
//            throw new Win32Exception(status);

//        try
//        {
//            return Process.GetProcessById(pbi.InheritedFromUniqueProcessId.ToInt32());
//        }
//        catch (ArgumentException)
//        {
//            // not found
//            return null;
//        }
//    }
//}
