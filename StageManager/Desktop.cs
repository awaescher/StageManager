using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.Text;
using workspacer;

namespace StageManager
{
	internal class Desktop
	{
		[DllImport("user32.dll", SetLastError = true)]
		static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		static extern IntPtr SendMessage(IntPtr hWnd, UInt32 Msg, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll", SetLastError = true)]
		public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

		[DllImport("user32.dll", SetLastError = false)]
		static extern IntPtr GetDesktopWindow();

		private const int WM_COMMAND = 0x111;
		private IntPtr _desktopViewHandle;

		public void TrySetDesktopView(IntPtr handle)
		{
			var buffer = new StringBuilder(255);
			Win32.GetClassName(handle, buffer, buffer.Capacity + 1);
			if (buffer.ToString() == "WorkerW")
				_desktopViewHandle = handle;
		}

		public bool GetDesktopIconsVisible()
		{
			// pinvoke suggestions from StackOverflow are not working reliably, so we read the registry directly
			// https://stackoverflow.com/questions/6402834/how-to-hide-desktop-icons-programmatically
			using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", writable: false);
			if (key?.GetValue("HideIcons", 0) is int hideIconsValue)
				return hideIconsValue == 0;

			return false;
		}

		private void ToggleDesktopIcons()
		{
			var toggleDesktopCommand = new IntPtr(0x7402);
			SendMessage(GetDesktopSHELLDLL_DefView(), WM_COMMAND, toggleDesktopCommand, IntPtr.Zero);
		}

		public void ShowIcons()
		{
			if (!GetDesktopIconsVisible())
				ToggleDesktopIcons();
		}

		public void HideIcons()
		{
			if (GetDesktopIconsVisible())
				ToggleDesktopIcons();
		}

		static IntPtr GetDesktopSHELLDLL_DefView()
		{
			var hShellViewWin = IntPtr.Zero;
			var hWorkerW = IntPtr.Zero;

			var hProgman = FindWindow("Progman", "Program Manager");
			var hDesktopWnd = GetDesktopWindow();

			// If the main Program Manager window is found
			if (hProgman != IntPtr.Zero)
			{
				// Get and load the main List view window containing the icons.
				hShellViewWin = FindWindowEx(hProgman, IntPtr.Zero, "SHELLDLL_DefView", null);

				if (hShellViewWin == IntPtr.Zero)
				{
					// When this fails (picture rotation is turned ON, toggledesktop shell cmd used ), then look for the WorkerW windows list to get the
					// correct desktop list handle.
					// As there can be multiple WorkerW windows, iterate through all to get the correct one
					do
					{
						hWorkerW = FindWindowEx(hDesktopWnd, hWorkerW, "WorkerW", null);
						hShellViewWin = FindWindowEx(hWorkerW, IntPtr.Zero, "SHELLDLL_DefView", null);
					} while (hShellViewWin == IntPtr.Zero && hWorkerW != IntPtr.Zero);
				}
			}
			return hShellViewWin;
		}

		public bool HasDesktopView => _desktopViewHandle != IntPtr.Zero;

		public IntPtr DesktopViewHandle => _desktopViewHandle;
	}
}
