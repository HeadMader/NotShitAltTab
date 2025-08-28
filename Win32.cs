using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using WinRT.Interop;
using System.Drawing;

namespace NotShitAltTab;
public static class Win32
{



	// ---------- window handle helpers ----------
	public static nint GetHwnd(Window window) => WindowNative.GetWindowHandle(window);
	public static WindowId GetWindowIdFromHwnd(nint hwnd) => Win32Interop.GetWindowIdFromWindow(hwnd);

	// ---------- TopMost ----------
	public static void SetTopMost(nint hwnd, bool enable)
	{
		var insertAfter = enable ? (nint)(-1) /*HWND_TOPMOST*/ : (nint)(-2) /*HWND_NOTOPMOST*/;
		SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0,
				SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
	}

	// ---------- RegisterHotKey ----------
	[DllImport("user32.dll")] public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);
	[DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hWnd, int id);

	// ---------- Message hook ----------
	public delegate nint HotkeyWndProc(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled);

	public static void HwndSourceHook(nint hwnd, HotkeyWndProc proc)
			=> Subclass.Add(hwnd, proc);

	// ---------- Enum/props ----------
	public delegate bool EnumWindowsProc(nint hWnd, nint lParam);
	[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);
	[DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hWnd);
	[DllImport("user32.dll")] public static extern int GetWindowLong(nint hWnd, int nIndex);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint hWnd, System.Text.StringBuilder lpString, int nMaxCount);
	public static string GetWindowText(nint h)
	{
		var sb = new System.Text.StringBuilder(512);
		GetWindowText(h, sb, sb.Capacity);
		return sb.ToString();
	}

	[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

	// ---------- DWM Cloaked ----------
	[DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
	public static bool IsCloaked(nint hwnd)
	{
		const int DWMWA_CLOAKED = 14;
		if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0)
			return cloaked != 0;
		return false;
	}

	// ---------- Activate target ----------
	[DllImport("user32.dll")] static extern bool SetForegroundWindow(nint hWnd);
	[DllImport("user32.dll")] static extern bool ShowWindowAsync(nint hWnd, int nCmdShow);
	[DllImport("user32.dll")] static extern bool IsIconic(nint hWnd);
	[DllImport("user32.dll")] static extern nint GetForegroundWindow();
	[DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hWnd, IntPtr pid);
	[DllImport("user32.dll")] static extern uint GetCurrentThreadId();
	[DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

	public static void BringToFront(nint hwnd)
	{
		const int SW_RESTORE = 9;
		if (IsIconic(hwnd)) ShowWindowAsync(hwnd, SW_RESTORE);

		// try direct
		if (SetForegroundWindow(hwnd)) return;

		// fallback via AttachThreadInput
		var fg = GetForegroundWindow();
		uint targetTid = GetWindowThreadProcessId(hwnd, IntPtr.Zero);
		uint fgTid = GetWindowThreadProcessId(fg, IntPtr.Zero);
		uint curTid = GetCurrentThreadId();

		AttachThreadInput(curTid, targetTid, true);
		AttachThreadInput(curTid, fgTid, true);
		SetForegroundWindow(hwnd);
		AttachThreadInput(curTid, targetTid, false);
		AttachThreadInput(curTid, fgTid, false);
	}

	// ---------- Icons (best-effort) ----------
	[DllImport("user32.dll")] static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);
	[DllImport("user32.dll")] static extern bool DestroyIcon(nint hIcon);
	[DllImport("gdi32.dll")] static extern bool DeleteObject(nint hObject);

	public static BitmapImage? GetAppIcon(nint hwnd)
	{
		// Try WM_GETICON
		const int WM_GETICON = 0x7F;
		const int ICON_SMALL2 = 2;
		nint hIcon = SendMessage(hwnd, WM_GETICON, (nint)ICON_SMALL2, 0);
		if (hIcon == 0) return null;

		using var icon = Icon.FromHandle(hIcon);
		// Add this at the top of the file with the other using directives

		using var bmp = icon.ToBitmap();
		var path = System.IO.Path.GetTempFileName();
		bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
		var img = new BitmapImage(new Uri(path));
		// cleanup best-effort
		DestroyIcon(hIcon);
		return img;
	}

	// ---------- SetWindowPos ----------
	[DllImport("user32.dll", SetLastError = true)]
	static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
	const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
}

internal static class Subclass
{
	// очень маленький сабклассер окна для ловли WM_HOTKEY
	private const int GWLP_WNDPROC = -4;
	private static nint _old;
	private static Win32.HotkeyWndProc? _cb;


	[DllImport("user32.dll")] static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);
	[DllImport("user32.dll")] static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);

	public static void Add(nint hwnd, Win32.HotkeyWndProc callback)
	{
		_cb = callback;
		_old = SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate((WndProc)Hook));
		_hwnd = hwnd;
	}

	private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);
	private static nint _hwnd;

	private static nint Hook(nint hWnd, uint msg, nint wParam, nint lParam)
	{
		bool handled = false;
		var ret = _cb?.Invoke(hWnd, msg, wParam, lParam, ref handled) ?? 0;
		if (handled) return ret;
		return CallWindowProc(_old, hWnd, msg, wParam, lParam);
	}
}