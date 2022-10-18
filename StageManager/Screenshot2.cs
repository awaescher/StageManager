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
	internal class Screenshot2
	{
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
						g.CopyFromScreen(rect.Left, rect.Top, 0, 0, bmp.Size);
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
				Debug.WriteLine(sw.Elapsed);
			}
			
		}
	}
}
