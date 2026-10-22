using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PortDeck.Services;

/// <summary>
/// Reads the current working directory of another process from its PEB.
/// This is what makes project detection work for commands such as
/// <c>node server.js</c>, where the script path is relative.
/// </summary>
public static class ProcessWorkingDirectory
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessVmRead = 0x0010;
    private const int ProcessBasicInformationClass = 0;

    public static string? TryGet(int pid)
    {
        if (pid <= 0) return null;

        var handle = OpenProcess(ProcessQueryLimitedInformation | ProcessVmRead, false, pid);
        if (handle == IntPtr.Zero) return null;

        try
        {
            var isWow64 = IsWow64Process(handle, out var wow64) && wow64;
            var info = new ProcessBasicInformation();
            var status = NtQueryInformationProcess(
                handle,
                ProcessBasicInformationClass,
                ref info,
                Marshal.SizeOf<ProcessBasicInformation>(),
                out _);

            if (status != 0 || info.PebBaseAddress == IntPtr.Zero) return null;

            // Try the native layout first. WOW64 processes sometimes expose a
            // 64-bit PEB and sometimes a 32-bit one depending on the target.
            var result = ReadCurrentDirectory(handle, info.PebBaseAddress, is32Bit: false);
            if (string.IsNullOrWhiteSpace(result) && isWow64)
                result = ReadCurrentDirectory(handle, info.PebBaseAddress, is32Bit: true);

            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static string? ReadCurrentDirectory(IntPtr process, IntPtr pebAddress, bool is32Bit)
    {
        var pointerSize = is32Bit ? 4 : 8;
        var processParametersOffset = is32Bit ? 0x10 : 0x20;
        var processParameters = ReadPointer(
            process,
            IntPtr.Add(pebAddress, processParametersOffset),
            pointerSize);

        if (processParameters == IntPtr.Zero) return null;

        // RTL_USER_PROCESS_PARAMETERS.CurrentDirectory is a CURDIR containing
        // a UNICODE_STRING followed by a directory handle.
        var currentDirectoryOffset = is32Bit ? 0x24 : 0x38;
        var length = ReadUInt16(process, IntPtr.Add(processParameters, currentDirectoryOffset));
        if (length == 0 || length > 32_767 || (length & 1) != 0) return null;

        var bufferOffset = currentDirectoryOffset + (is32Bit ? 4 : 8);
        var buffer = ReadPointer(process, IntPtr.Add(processParameters, bufferOffset), pointerSize);
        if (buffer == IntPtr.Zero) return null;

        var bytes = new byte[length];
        if (!ReadBytes(process, buffer, bytes)) return null;

        var path = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            return Path.TrimEndingDirectorySeparator(path);
        }
        catch
        {
            return path;
        }
    }

    private static IntPtr ReadPointer(IntPtr process, IntPtr address, int pointerSize)
    {
        var bytes = new byte[pointerSize];
        if (!ReadBytes(process, address, bytes)) return IntPtr.Zero;

        return pointerSize == 8
            ? (IntPtr)BitConverter.ToInt64(bytes, 0)
            : (IntPtr)BitConverter.ToInt32(bytes, 0);
    }

    private static ushort ReadUInt16(IntPtr process, IntPtr address)
    {
        var bytes = new byte[2];
        return ReadBytes(process, address, bytes)
            ? BitConverter.ToUInt16(bytes, 0)
            : (ushort)0;
    }

    private static bool ReadBytes(IntPtr process, IntPtr address, byte[] buffer) =>
        ReadProcessMemory(process, address, buffer, (IntPtr)buffer.Length, out var read) &&
        read.ToInt64() == buffer.Length;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        IntPtr process,
        IntPtr baseAddress,
        byte[] buffer,
        IntPtr size,
        out IntPtr numberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(IntPtr process, out bool wow64Process);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr process,
        int processInformationClass,
        ref ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);
}
