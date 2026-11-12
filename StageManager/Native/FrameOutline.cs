using StageManager.Core;
using StageManager.Native.Interop;
using System;
using System.Runtime.InteropServices;

namespace StageManager.Native
{
	/// <summary>
	/// The outline of a window, drawn the way Windows draws the frame of a window that is dragged without its contents: a thin frame with a
	/// halftone pattern, which shows on light and dark backgrounds alike. It consists of four plain windows (one per edge) that are moved in
	/// one batch. Moving them costs next to nothing, in contrast to a transparent window over the whole display that has to be redrawn.
	/// </summary>
	internal sealed class FrameOutline : IDisposable
	{
		private const string ClassName = "StageManagerFrameEdge";

		private const uint WS_POPUP = 0x80000000;
		private const uint WS_EX_TOPMOST = 0x00000008;
		private const uint WS_EX_TRANSPARENT = 0x00000020;
		private const uint WS_EX_TOOLWINDOW = 0x00000080;
		private const uint WS_EX_LAYERED = 0x00080000;
		private const uint WS_EX_NOACTIVATE = 0x08000000;

		private const uint SWP_NOSIZE = 0x0001;
		private const uint SWP_NOMOVE = 0x0002;
		private const uint SWP_NOZORDER = 0x0004;
		private const uint SWP_NOACTIVATE = 0x0010;
		private const uint SWP_SHOWWINDOW = 0x0040;

		private const uint LWA_ALPHA = 0x2;
		private const int ERROR_CLASS_ALREADY_EXISTS = 1410;

		private static readonly IntPtr HWND_TOPMOST = new(-1);

		private static bool _registered;

		private readonly IntPtr[] _edges = new IntPtr[4];
		private byte _alpha = 255;
		private bool _shown;

		public FrameOutline()
		{
			Register();

			for (var i = 0; i < _edges.Length; i++)
			{
				_edges[i] = CreateWindowExW(WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE,
					ClassName, "", WS_POPUP, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);

				if (_edges[i] == IntPtr.Zero)
				{
					Dispose();
					throw new InvalidOperationException($"The frame could not be created (error {Marshal.GetLastWin32Error()}).");
				}

				SetLayeredWindowAttributes(_edges[i], 0, 255, LWA_ALPHA);
				NativeMethods.SetTransitionsDisabled(_edges[i], true);
			}
		}

		/// <summary>Moves the frame, in physical screen pixels. The four edges change together.</summary>
		public void Update(double x, double y, double width, double height, double opacity, int thickness)
		{
			var alpha = (byte)Math.Clamp(Math.Round(255 * opacity), 0, 255);
			if (alpha != _alpha)
			{
				_alpha = alpha;
				foreach (var edge in _edges)
					SetLayeredWindowAttributes(edge, 0, alpha, LWA_ALPHA);
			}

			var rects = FrameEdges.Of(x, y, width, height, thickness);
			var flags = SWP_NOACTIVATE | SWP_NOZORDER | (_shown ? 0u : SWP_SHOWWINDOW);

			var batch = BeginDeferWindowPos(_edges.Length);
			for (var i = 0; i < _edges.Length && batch != IntPtr.Zero; i++)
				batch = DeferWindowPos(batch, _edges[i], IntPtr.Zero, rects[i].X, rects[i].Y, rects[i].Width, rects[i].Height, flags);

			if (batch != IntPtr.Zero)
				EndDeferWindowPos(batch);

			_shown = true;
		}

		/// <summary>Puts the frame straight below a window (or on top of all topmost windows), without moving it.</summary>
		public void PlaceBelow(IntPtr above)
		{
			var insertAfter = above != IntPtr.Zero && IsWindow(above) ? above : HWND_TOPMOST;

			foreach (var edge in _edges)
				SetWindowPos(edge, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
		}

		public void Dispose()
		{
			for (var i = 0; i < _edges.Length; i++)
			{
				if (_edges[i] == IntPtr.Zero)
					continue;

				// hide first: destroying a layered window can show its last picture once more
				ShowWindow(_edges[i], 0);
				DestroyWindow(_edges[i]);
				_edges[i] = IntPtr.Zero;
			}
		}

		private static void Register()
		{
			if (_registered)
				return;

			// the pattern Windows uses for its drag frame: every other pixel black and white, a mid gray from afar
			var bits = new byte[16];
			for (var row = 0; row < 8; row++)
				bits[row * 2] = (byte)(row % 2 == 0 ? 0xAA : 0x55);

			var bitmap = CreateBitmap(8, 8, 1, 1, bits);
			var brush = CreatePatternBrush(bitmap);

			var window = new WNDCLASSEX
			{
				cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
				// no code of our own: the edges only have to exist and be painted with the brush of the class
				lpfnWndProc = GetProcAddress(GetModuleHandleW("user32.dll"), "DefWindowProcW"),
				hInstance = GetModuleHandleW(null),
				hbrBackground = brush,
				lpszClassName = ClassName
			};

			if (RegisterClassExW(ref window) == 0 && Marshal.GetLastWin32Error() != ERROR_CLASS_ALREADY_EXISTS)
				throw new InvalidOperationException($"The frame class could not be registered (error {Marshal.GetLastWin32Error()}).");

			_registered = true;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct WNDCLASSEX
		{
			public uint cbSize;
			public uint style;
			public IntPtr lpfnWndProc;
			public int cbClsExtra;
			public int cbWndExtra;
			public IntPtr hInstance;
			public IntPtr hIcon;
			public IntPtr hCursor;
			public IntPtr hbrBackground;
			public string? lpszMenuName;
			public string lpszClassName;
			public IntPtr hIconSm;
		}

		[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern ushort RegisterClassExW(ref WNDCLASSEX windowClass);

		[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
			IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool DestroyWindow(IntPtr window);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool ShowWindow(IntPtr window, int command);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool IsWindow(IntPtr window);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

		[DllImport("user32.dll")]
		private static extern IntPtr BeginDeferWindowPos(int count);

		[DllImport("user32.dll")]
		private static extern IntPtr DeferWindowPos(IntPtr batch, IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool EndDeferWindowPos(IntPtr batch);

		[DllImport("gdi32.dll")]
		private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[] bits);

		[DllImport("gdi32.dll")]
		private static extern IntPtr CreatePatternBrush(IntPtr bitmap);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		private static extern IntPtr GetModuleHandleW(string? name);

		[DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
		private static extern IntPtr GetProcAddress(IntPtr module, string name);
	}
}
