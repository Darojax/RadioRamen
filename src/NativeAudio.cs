using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace RadioRamen;

internal static class NativeAudio
{
    internal const uint Float = 0x100, Decode = 0x200000, Block = 0x100000, Unicode = 0x80000000, Prescan = 0x20000;
    private const string BassHash = "FEBB2CF1882D554C3A958280777DA0B69F07DE6E262DF271DE11C56E4A54AFD4";
    private const string HlsHash = "9E970D27BD2048514F38A8C6A87009F2BD8AAA6D4CB29A3CA7ABF2035CFA3C6B";

    internal static void Load(string folder)
    {
        if (IntPtr.Size != 8) throw new PlatformNotSupportedException("RadioRamen requires Windows x64.");
        foreach (var file in new[] { ("bass.dll", BassHash), ("basshls.dll", HlsHash) })
        {
            string path = Path.Combine(folder, file.Item1);
            using var sha = SHA256.Create(); using var stream = File.OpenRead(path);
            string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            if (actual != file.Item2) throw new InvalidDataException("Audio library hash mismatch: " + file.Item1);
            IntPtr existing = GetModuleHandle(file.Item1);
            if (existing != IntPtr.Zero)
            {
                var loadedPath = new System.Text.StringBuilder(32768);
                GetModuleFileName(existing, loadedPath, loadedPath.Capacity);
                if (!string.Equals(Path.GetFullPath(loadedPath.ToString()), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Another plugin has loaded a different " + file.Item1 + ". RadioRamen retained the original radio.");
            }
            if (LoadLibrary(path) == IntPtr.Zero) throw new InvalidOperationException("Could not load " + file.Item1);
        }
        if (!Init(0, 48000, 0, IntPtr.Zero, IntPtr.Zero)) throw new InvalidOperationException("Audio decoder initialization failed: " + Error());
        if (PluginLoad(Path.Combine(folder, "basshls.dll"), Unicode) == 0)
        { int error = Error(); Free(); throw new InvalidOperationException("HLS decoder initialization failed: " + error); }
        SetConfig(11, 8000); SetConfig(37, 6000); SetConfig(12, 5000);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ChannelInfo { internal uint Frequency, Channels, Flags, Type, OriginalResolution, Plugin, Sample; internal IntPtr FileName; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void FileClose(IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate ulong FileLength(IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate uint FileRead(IntPtr buffer, uint length, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.Bool)] internal delegate bool FileSeek(ulong offset, IntPtr user);
    [StructLayout(LayoutKind.Sequential)] internal struct FileProcs { internal FileClose Close; internal FileLength Length; internal FileRead Read; internal FileSeek Seek; }
    [DllImport("bass.dll", EntryPoint = "BASS_StreamCreateFileUser", ExactSpelling = true)] internal static extern uint OpenUser(uint system, uint flags, ref FileProcs callbacks, IntPtr user);
    [DllImport("kernel32", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibrary(string path);
    [DllImport("kernel32", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", EntryPoint = "GetModuleFileNameW", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileName(IntPtr handle, System.Text.StringBuilder path, int size);
    [DllImport("bass.dll", EntryPoint = "BASS_Init")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Init(int device, uint frequency, uint flags, IntPtr window, IntPtr clsid);
    [DllImport("bass.dll", EntryPoint = "BASS_SetDevice")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetDevice(uint device);
    [DllImport("bass.dll", EntryPoint = "BASS_SetConfig")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetConfig(uint option, uint value);
    [DllImport("bass.dll", EntryPoint = "BASS_PluginLoad", CharSet = CharSet.Unicode)] internal static extern uint PluginLoad(string file, uint flags);
    [DllImport("bass.dll", EntryPoint = "BASS_StreamCreateURL", CharSet = CharSet.Unicode)] internal static extern uint OpenUrl(string url, uint offset, uint flags, IntPtr callback, IntPtr user);
    [DllImport("bass.dll", EntryPoint = "BASS_StreamCreateFile", CharSet = CharSet.Unicode)] internal static extern uint OpenFile([MarshalAs(UnmanagedType.Bool)] bool memory, string file, ulong offset, ulong length, uint flags);
    [DllImport("bass.dll", EntryPoint = "BASS_ChannelGetData")] internal static extern int Read(uint channel, [Out] float[] data, uint length);
    [DllImport("bass.dll", EntryPoint = "BASS_ChannelGetInfo")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Info(uint channel, out ChannelInfo info);
    [DllImport("bass.dll", EntryPoint = "BASS_ChannelSetPosition")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetPosition(uint channel, ulong position, uint mode);
    [DllImport("bass.dll", EntryPoint = "BASS_ChannelGetPosition")] internal static extern ulong GetPosition(uint channel, uint mode);
    [DllImport("bass.dll", EntryPoint = "BASS_ChannelGetLength")] internal static extern ulong Length(uint channel, uint mode);
    [DllImport("bass.dll", EntryPoint = "BASS_ChannelSeconds2Bytes")] internal static extern ulong SecondsToBytes(uint channel, double seconds);
    [DllImport("bass.dll", EntryPoint = "BASS_ChannelBytes2Seconds")] internal static extern double BytesToSeconds(uint channel, ulong bytes);
    [DllImport("bass.dll", EntryPoint = "BASS_ErrorGetCode")] internal static extern int Error();
    [DllImport("bass.dll", EntryPoint = "BASS_StreamFree")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Close(uint channel);
    [DllImport("bass.dll", EntryPoint = "BASS_StreamCancel")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Cancel(IntPtr cookie);
    [DllImport("bass.dll", EntryPoint = "BASS_Free")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Free();
}
