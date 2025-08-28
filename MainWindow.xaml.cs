using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace NotShitAltTab;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : Window
{
	private nint _hwnd;
	private int _hotkeyId = 1;
	private string _typeBuffer = "";
	private Timer _bufferTimer = new(900);
	private List<WinEntry> _all = [];
	private List<WinEntry> _filtered = [];
	private char? _filterLetter = null;
	private const int x = 1720; // half of 3440px, for centering on a 4K monitor
	private const int y = 720; // half of 1440px, for centering on a 4K monitor
	public MainWindow()
	{
		this.InitializeComponent();
		this.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x-350, y-350, 700, 700));
		Title = "Not shit Alt-Tab";
		_hwnd = Win32.GetHwnd(this);
		MakeTopMost();
		RegisterHotKey(); // Alt+` by default
		_bufferTimer.Elapsed += (_, __) => DispatcherQueue.TryEnqueue(() => { _typeBuffer = ""; Hint.Text = ""; });
		this.Closed += (_, __) => Win32.UnregisterHotKey(_hwnd, _hotkeyId);
		Win32.HwndSourceHook(_hwnd, WndProc);
		HideOverlay();
	}

	#region Window chrome/top-most
	private void MakeTopMost()
	{
		Win32.SetTopMost(_hwnd, true);
		var appWindow = AppWindow.GetFromWindowId(Win32.GetWindowIdFromHwnd(_hwnd));
		appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
		appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
	}
	#endregion

	#region Hotkey & WndProc
	private void RegisterHotKey()
	{
		// MOD_ALT (0x0001) + VK_OEM_3 (`~ key)
		if (!Win32.RegisterHotKey(_hwnd, _hotkeyId, 0x0001, 0xC0))
			Hint.Text = "Failed to register hotkey Alt+`";
	}

	private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
	{
		const int WM_HOTKEY = 0x0312;
		if (msg == WM_HOTKEY && wParam == (nint)_hotkeyId)
		{
			if (this.AppWindow.IsVisible) HideOverlay(); else ShowOverlay();
			handled = true;
		}
		return 0;
	}
	#endregion

	private void ShowOverlay()
	{
		_all = WinEnum.GetTopLevelWindows();
		ClearFilter();                 // ensure fresh state
		WindowsList.ItemsSource = _all;
		if (_all.Count > 0) WindowsList.SelectedIndex = 0;
		this.AppWindow.Show();
		// Ensure our overlay window takes the foreground and receives keyboard focus
		Win32.BringToFront(_hwnd);
		Activate();
		WindowsList.Focus(FocusState.Programmatic);
	}

	private void HideOverlay()
	{
		this.AppWindow.Hide();
		_typeBuffer = "";
		Hint.Text = "";
	}

	private void ClearFilter()
	{
		_filterLetter = null;
		_filtered.Clear();
		Hint.Text = "";
	}

	private void Grid_KeyDown(object sender, KeyRoutedEventArgs e)
	{
		// 1) Esc / Backspace
		if (e.Key == Windows.System.VirtualKey.Escape)
		{
			if (_filterLetter != null)
			{
				// clear filter, keep overlay open
				ClearFilter();
				WindowsList.ItemsSource = _all;
				WindowsList.SelectedIndex = 0;
				return;
			}
			HideOverlay();
			return;
		}
		if (e.Key == Windows.System.VirtualKey.Back)
		{
			if (_filterLetter != null)
			{
				ClearFilter();
				WindowsList.ItemsSource = _all;
				WindowsList.SelectedIndex = 0;
				return;
			}
		}

		// 2) Confirm
		if (e.Key == Windows.System.VirtualKey.Enter)
		{
			if (WindowsList.SelectedItem is WinEntry win) ActivateWindow(win);
			return;
		}

		// 3) Letter/digit → START/RESET filter (includes W and S!)
		char c = KeyToChar(e);
		if (char.IsLetterOrDigit(c) && _filterLetter == null)
		{
			// start (or restart) single-letter filter
			_filterLetter = char.ToLowerInvariant(c);
			ApplyFilterAndMaybeActivate();
			return;
		}

		// 4) Navigation with W/S only when filter is active and there are multiple items
		if (_filterLetter != null && WindowsList.Items.Count > 1 &&
				(e.Key == Windows.System.VirtualKey.W || e.Key == Windows.System.VirtualKey.Up ||
				 e.Key == Windows.System.VirtualKey.S || e.Key == Windows.System.VirtualKey.Down))
		{
			int dir = (e.Key == Windows.System.VirtualKey.W || e.Key == Windows.System.VirtualKey.Up) ? -1 : 1;
			MoveSelection(dir);
			e.Handled = true;
			return;
		}
	}

	private void ApplyFilterAndMaybeActivate()
	{
		if (_filterLetter == null) return;

		_filtered = [.. _all.Where(w => w.ProcessName.StartsWith(_filterLetter.Value.ToString(), StringComparison.InvariantCultureIgnoreCase))];

		if (_filtered.Count == 0)
		{
			// no hits → show all, keep hint
			WindowsList.ItemsSource = _all;
			WindowsList.SelectedIndex = 0;
			Hint.Text = $"No apps starting with '{char.ToUpper(_filterLetter.Value)}'";
			// keep filter letter so user can Esc/Backspace to clear
			return;
		}

		if (_filtered.Count == 1)
		{
			// instant switch
			var only = _filtered[0];
			HideOverlay();
			Win32.BringToFront(only.Hwnd);
			return;
		}

		// multiple → show only matches and let user W/S cycle
		WindowsList.ItemsSource = _filtered;
		WindowsList.SelectedIndex = 0;
		WindowsList.ScrollIntoView(WindowsList.SelectedItem);
		Hint.Text = $"{_filtered.Count} apps — use W/S (or ↑/↓) to select, Enter to switch";
	}

	private void MoveSelection(int direction)
	{
		if (WindowsList.Items == null || WindowsList.Items.Count == 0) return;
		int count = WindowsList.Items.Count;
		int cur = WindowsList.SelectedIndex < 0 ? 0 : WindowsList.SelectedIndex;
		int next = (cur + direction + count) % count;
		WindowsList.SelectedIndex = next;
		WindowsList.ScrollIntoView(WindowsList.SelectedItem);
	}

	private static char KeyToChar(KeyRoutedEventArgs e)
	{
		var key = e.Key;
		if (key >= Windows.System.VirtualKey.A && key <= Windows.System.VirtualKey.Z)
			return (char)('a' + (key - Windows.System.VirtualKey.A));
		if (key >= Windows.System.VirtualKey.Number0 && key <= Windows.System.VirtualKey.Number9)
			return (char)('0' + (key - Windows.System.VirtualKey.Number0));
		// Numpad digits (optional)
		if (key >= Windows.System.VirtualKey.NumberPad0 && key <= Windows.System.VirtualKey.NumberPad9)
			return (char)('0' + (key - Windows.System.VirtualKey.NumberPad0));
		return '\0';
	}


	private void WindowsList_ItemClick(object sender, Microsoft.UI.Xaml.Controls.ItemClickEventArgs e)
	{
		if (e.ClickedItem is WinEntry win) ActivateWindow(win);
	}
	private void WindowsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
	{
		if (WindowsList.SelectedItem is WinEntry win) ActivateWindow(win);
	}


	private void ActivateWindow(WinEntry win)
	{
		HideOverlay();
		Win32.BringToFront(win.Hwnd);
	}
}
