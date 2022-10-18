using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StageManager
{
	internal class Screenshot3
	{
		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool PrintWindow(IntPtr hwnd, IntPtr hDC, uint nFlags);

		const int PW_RENDERFULLCONTENT = 2;

		public static Bitmap CaptureWindowRelativeToDesktop(IntPtr handle)
		{
			Rectangle rect = Screenshot.GetWindowRectangle(handle);

			if (rect.IsEmpty)
				return null;

			var screenBounds = Screen.FromHandle(handle).WorkingArea.Size;
			var screenBmp = new Bitmap(screenBounds.Width, screenBounds.Height);

			// Create a bitmap of the appropriate size to receive the screenshot.
			var windowBmp = new Bitmap(rect.Width, rect.Height);

			// Draw the screenshot into our bitmap.
			using (var g = Graphics.FromImage(windowBmp))
			{
				IntPtr dc = g.GetHdc();

				// Grab a copy of the window. Use PrintWindow because it works even when the
				// window's partially occluded. The PW_RENDERFULLCONTENT flag is undocumented,
				// but works starting in Windows 8.1. It allows for capturing the contents of
				// the window that are drawn using DirectComposition.
				bool success = PrintWindow(handle, dc, PW_RENDERFULLCONTENT);
				g.ReleaseHdc(dc);
			}

			using (var g = Graphics.FromImage(screenBmp))
			{
				g.DrawImageUnscaled(windowBmp, rect.Location);
			}

			//using (var stream = new MemoryStream())
			//{
			//	bmp.Save(stream, ImageFormat.Png);
			//	return (Bitmap)Bitmap.FromStream(stream);
			//}
			return screenBmp;
		}

		public static Bitmap CaptureWindow(IntPtr handle)
		{
			var sw = Stopwatch.StartNew();

			try
			{
				Rectangle rect = Screenshot.GetWindowRectangle(handle);

				if (rect.IsEmpty)
					return null;

				// Create a bitmap of the appropriate size to receive the screenshot.
				using (Bitmap bmp = new Bitmap(rect.Width, rect.Height))
				{
					// Draw the screenshot into our bitmap.
					using (Graphics g = Graphics.FromImage(bmp))
					{
						IntPtr dc = g.GetHdc();

						// Grab a copy of the window. Use PrintWindow because it works even when the
						// window's partially occluded. The PW_RENDERFULLCONTENT flag is undocumented,
						// but works starting in Windows 8.1. It allows for capturing the contents of
						// the window that are drawn using DirectComposition.
						bool success = PrintWindow(handle, dc, PW_RENDERFULLCONTENT);
						g.ReleaseHdc(dc);
					}

					using (var stream = new MemoryStream())
					{
						bmp.Save(stream, ImageFormat.Jpeg);
						return (Bitmap)Bitmap.FromStream(stream);
					}
				}
			}
			finally
			{
				sw.Stop();
				Debug.WriteLine($"{handle} {sw.Elapsed}");
			}
		}
	}
}
