using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Services;

namespace PhantomMonitor.App.Views;

public sealed partial class OverlayWindow
{
    private DispatcherTimer? _followTimer;
    private DispatcherTimer? _topmostTimer;
    private static uint _explorerPid;
    private static DateTime _explorerPidCheckedAt = DateTime.MinValue;

    private void StartWindowsTimers()
    {
        if (_followTimer is null)
        {
            _followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _followTimer.Tick += (_, _) => PositionWindows();
        }
        _followTimer.Start();
        if (_topmostTimer is null)
        {
            _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _topmostTimer.Tick += (_, _) => MaintainTopmost();
        }
        _topmostTimer.Start();
    }

    private void StopWindowsTimers()
    {
        _followTimer?.Stop();
        _topmostTimer?.Stop();
    }

    private void MaintainTopmost()
    {
        foreach (var window in _windows.Values)
        {
            if (!window.IsVisible || window.TryGetPlatformHandle()?.Handle is not { } handle) continue;
            SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }

    private void MakeClickThrough(OverlayBlockWindow window)
    {
        if (window.TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero) return;
        var style = GetWindowLongPtr(handle, GwlExstyle).ToInt64();
        SetWindowLongPtr(handle, GwlExstyle,
            new IntPtr(style | WsExLayered | WsExTransparent | WsExTopmost | WsExNoActivate | WsExToolWindow));
    }

    [SupportedOSPlatform("windows")]
    private void PositionWindowsOnWindows()
    {
        var foreground = Win32Interop.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return;
        Win32Interop.GetWindowThreadProcessId(foreground, out var pid);
        if (pid == (uint)Environment.ProcessId || pid == GetExplorerPid()) return;
        if (!GetWindowRect(foreground, out var rect) || rect.Left <= -32000) return;

        _viewModel.UpdateProfileForForeground(pid);
        var referenceWindow = _windows.Values.FirstOrDefault(window => window.IsVisible);
        var scale = referenceWindow?.RenderScaling ?? 1;
        var width = (rect.Right - rect.Left) / scale;
        var height = (rect.Bottom - rect.Top) / scale;
        _viewModel.SetOverlayCanvasSize(width, height);

        foreach (var (key, window) in _windows)
        {
            if (!window.IsVisible) continue;
            var block = (OverlayCanvasBlockViewModel)window.DataContext!;
            var left = rect.Left / scale + _viewModel.FollowOffsetX + block.X * Math.Max(0, width - window.Width);
            var top = rect.Top / scale + _viewModel.FollowOffsetY + block.Y * Math.Max(0, height - window.Height);
            window.Position = new PixelPoint((int)(left * scale), (int)(top * scale));
        }
    }

    private static uint GetExplorerPid()
    {
        if (DateTime.UtcNow - _explorerPidCheckedAt < TimeSpan.FromSeconds(10))
            return _explorerPid;
        _explorerPidCheckedAt = DateTime.UtcNow;
        try
        {
            var process = Process.GetProcessesByName("explorer").FirstOrDefault();
            if (process is null) return _explorerPid = 0;
            using (process) return _explorerPid = (uint)process.Id;
        }
        catch { return _explorerPid = 0; }
    }

    private const int GwlExstyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExLayered = 0x00080000;
    private const long WsExTopmost = 0x00000008;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExToolWindow = 0x00000080;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) => IntPtr.Size == 4
        ? new IntPtr(GetWindowLong32(hwnd, index)) : GetWindowLongPtr64(hwnd, index);
    private static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value) => IntPtr.Size == 4
        ? new IntPtr(SetWindowLong32(hwnd, index, (int)value)) : SetWindowLongPtr64(hwnd, index, value);
}
