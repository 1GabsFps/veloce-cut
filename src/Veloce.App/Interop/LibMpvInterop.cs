using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Veloce.App.Interop;

public static class LibMpvInterop
{
    private const string DllName = "mpv-2.dll";

    static LibMpvInterop()
    {
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(LibMpvInterop).Assembly, (name, assembly, path) =>
            {
                if (name == DllName || name == "libmpv-2.dll" || name == "mpv-1.dll")
                {
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    var p1 = Path.Combine(baseDir, "mpv-2.dll");
                    if (File.Exists(p1) && NativeLibrary.TryLoad(p1, out var h1)) return h1;

                    var p2 = Path.Combine(baseDir, "libmpv-2.dll");
                    if (File.Exists(p2) && NativeLibrary.TryLoad(p2, out var h2)) return h2;
                }
                return IntPtr.Zero;
            });
        }
        catch
        {
            // Ignore if resolver cannot be registered
        }
    }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_create();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_initialize(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_option_string(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_command(IntPtr handle, IntPtr[] args);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_command_string(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string args);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_property_string(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_get_property_string(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_free(IntPtr data);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_terminate_destroy(IntPtr handle);

    public static int MpvCommand(IntPtr handle, params string[] args)
    {
        if (handle == IntPtr.Zero || args == null || args.Length == 0) return -1;

        var ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            }
            ptrs[args.Length] = IntPtr.Zero; // Null-terminated array required by libmpv

            return mpv_command(handle, ptrs);
        }
        finally
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (ptrs[i] != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(ptrs[i]);
                }
            }
        }
    }

    public static bool IsMpvAvailable()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var p1 = Path.Combine(baseDir, "mpv-2.dll");
            var p2 = Path.Combine(baseDir, "libmpv-2.dll");

            if (File.Exists(p1) && NativeLibrary.TryLoad(p1, out var h1)) return true;
            if (File.Exists(p2) && NativeLibrary.TryLoad(p2, out var h2)) return true;

            return NativeLibrary.TryLoad(DllName, out var handle) ||
                   NativeLibrary.TryLoad("libmpv-2.dll", out handle) ||
                   NativeLibrary.TryLoad("mpv-1.dll", out handle);
        }
        catch
        {
            return false;
        }
    }
}
