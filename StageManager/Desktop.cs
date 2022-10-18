using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace StageManager
{
	internal class Desktop
	{
		[DllImport("user32.dll", SetLastError = true)] static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
		[DllImport("user32.dll", SetLastError = true)] static extern IntPtr GetWindow(IntPtr hWnd, GetWindow_Cmd uCmd);
		enum GetWindow_Cmd : uint
		{
			GW_HWNDFIRST = 0,
			GW_HWNDLAST = 1,
			GW_HWNDNEXT = 2,
			GW_HWNDPREV = 3,
			GW_OWNER = 4,
			GW_CHILD = 5,
			GW_ENABLEDPOPUP = 6
		}
		[DllImport("user32.dll", CharSet = CharSet.Auto)] static extern IntPtr SendMessage(IntPtr hWnd, UInt32 Msg, IntPtr wParam, IntPtr lParam);

		private const int WM_COMMAND = 0x111;

		[return: MarshalAs(UnmanagedType.Bool)]
		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool GetWindowInfo(IntPtr hwnd, ref WINDOWINFO pwi);

		[StructLayout(LayoutKind.Sequential)]
		public struct RECT
		{
			private int _Left;
			private int _Top;
			private int _Right;
			private int _Bottom;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct WINDOWINFO
		{
			public uint cbSize;
			public RECT rcWindow;
			public RECT rcClient;
			public uint dwStyle;
			public uint dwExStyle;
			public uint dwWindowStatus;
			public uint cxWindowBorders;
			public uint cyWindowBorders;
			public ushort atomWindowType;
			public ushort wCreatorVersion;

			public WINDOWINFO(Boolean? filler)
				: this()   // Allows automatic initialization of "cbSize" with "new WINDOWINFO(null/true/false)".
			{
				cbSize = (UInt32)(Marshal.SizeOf(typeof(WINDOWINFO)));
			}

		}

		static bool IsVisible()
		{
			IntPtr hWnd = GetWindow(GetWindow(FindWindow("Progman", "Program Manager"), GetWindow_Cmd.GW_CHILD), GetWindow_Cmd.GW_CHILD);
			WINDOWINFO info = new WINDOWINFO();
			info.cbSize = (uint)Marshal.SizeOf(info);
			GetWindowInfo(hWnd, ref info);
			return (info.dwStyle & 0x10000000) == 0x10000000;
		}

		static void ToggleDesktopIcons()
		{
			var toggleDesktopCommand = new IntPtr(0x7402);
			IntPtr hWnd = GetWindow(FindWindow("Progman", "Program Manager"), GetWindow_Cmd.GW_CHILD);
			SendMessage(hWnd, WM_COMMAND, toggleDesktopCommand, IntPtr.Zero);
		}


		public void ShowIcons()
		{
			if (!IsVisible())
				ToggleDesktopIcons();
		}

		public void HideIcons()
		{
			if (IsVisible())
				ToggleDesktopIcons();
		}

		//[DllImport("user32.dll", SetLastError = true)]
		//public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);
		//[DllImport("user32.dll", SetLastError = false)]
		//static extern IntPtr GetDesktopWindow();

		//static IntPtr GetDesktopSHELLDLL_DefView()
		//{
		//	var hShellViewWin = IntPtr.Zero;
		//	var hWorkerW = IntPtr.Zero;

		//	var hProgman = FindWindow("Progman", "Program Manager");
		//	var hDesktopWnd = GetDesktopWindow();

		//	// If the main Program Manager window is found
		//	if (hProgman != IntPtr.Zero)
		//	{
		//		// Get and load the main List view window containing the icons.
		//		hShellViewWin = FindWindowEx(hProgman, IntPtr.Zero, "SHELLDLL_DefView", null);
		//		if (hShellViewWin == IntPtr.Zero)
		//		{
		//			// When this fails (picture rotation is turned ON, toggledesktop shell cmd used ), then look for the WorkerW windows list to get the
		//			// correct desktop list handle.
		//			// As there can be multiple WorkerW windows, iterate through all to get the correct one
		//			do
		//			{
		//				hWorkerW = FindWindowEx(hDesktopWnd, hWorkerW, "WorkerW", null);
		//				hShellViewWin = FindWindowEx(hWorkerW, IntPtr.Zero, "SHELLDLL_DefView", null);
		//			} while (hShellViewWin == IntPtr.Zero && hWorkerW != IntPtr.Zero);
		//		}
		//	}
		//	return hShellViewWin;
		//}
	}
}
