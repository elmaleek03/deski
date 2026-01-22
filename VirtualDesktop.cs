using System;
using System.Runtime.InteropServices;

internal static class VirtualDesktop
{
    [DllImport("VirtualDesktopAccessor.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetCurrentDesktopNumber();

    [DllImport("VirtualDesktopAccessor.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetDesktopCount();

    [DllImport("VirtualDesktopAccessor.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void GoToDesktopNumber(int number);

    [DllImport("VirtualDesktopAccessor.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void MoveWindowToDesktopNumber(IntPtr hwnd, int number);

    [DllImport("VirtualDesktopAccessor.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool IsPinnedWindow(IntPtr hwnd);

    [DllImport("VirtualDesktopAccessor.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void PinWindow(IntPtr hwnd);

    [DllImport("VirtualDesktopAccessor.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void UnPinWindow(IntPtr hwnd);
}
