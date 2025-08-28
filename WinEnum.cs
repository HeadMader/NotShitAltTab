using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NotShitAltTab;

public record WinEntry(nint Hwnd, string Title, string ProcessName, BitmapImage? Icon);

public static class WinEnum
{
	public static List<WinEntry> GetTopLevelWindows()
	{
		var result = new List<WinEntry>();
		Win32.EnumWindows((hWnd, lParam) =>
		{
			if (!Win32.IsWindowVisible(hWnd)) return true;
			if (Win32.IsCloaked(hWnd)) return true; // пропускаем скрытые DWM
			var styleEx = Win32.GetWindowLong(hWnd, -20);
			const int WS_EX_TOOLWINDOW = 0x00000080;
			if ((styleEx & WS_EX_TOOLWINDOW) != 0) return true;

			var title = Win32.GetWindowText(hWnd);
			if (string.IsNullOrWhiteSpace(title)) return true;

			Win32.GetWindowThreadProcessId(hWnd, out uint pid);
			string procName = "app";
			try
			{
				using var p = Process.GetProcessById((int)pid);
				procName = p.ProcessName;
			}
			catch { }

			var icon = Win32.GetAppIcon(hWnd);
			result.Add(new WinEntry(hWnd, title, procName, icon));
			return true;
		}, 0);
		// Стабильная сортировка: по заголовку, затем по времени создания (прибл — pid)
		result.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.InvariantCultureIgnoreCase));
		return result;
	}
}