using StageManager.Native;
using StageManager.Native.Window;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StageManager.Model
{
	[System.Diagnostics.DebuggerDisplay("{Title}")]
	public class WindowModel : INotifyPropertyChanged
	{
		//If you get 'dllimport unknown'-, then add 'using System.Runtime.InteropServices;'
		[DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool DeleteObject([In] IntPtr hObject);

		private IWindow _window;
		private ImageSource _iconSource;
		private ImageSource _image;

		public event PropertyChangedEventHandler PropertyChanged;

		public WindowModel(IWindow window)
		{
			Window = window ?? throw new ArgumentNullException(nameof(window));
		}

		private void RaisePropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(memberName));
		}

		public string Title => _window.Title.Length > 20 ? _window.Title.Substring(0, 17) + " ..." : _window.Title;

		public ImageSource Image
		{
			get => _image;
			set
			{
				_image = value;
				RaisePropertyChanged();
			}
		}

		public ImageSource ImageSourceFromBitmap(System.Drawing.Bitmap bmp)
		{
			if (bmp is null)
				return null;

			var handle = bmp.GetHbitmap();
			try
			{
				return Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
			}
			finally { DeleteObject(handle); }
		}

		public static ImageSource IconToImageSource(System.Drawing.Icon icon)
		{
			if (icon is null)
				return null;

			//// TODO check memory leaks

			var bmp = System.Drawing.Bitmap.FromHicon(icon.Handle);

			var imageSource = Imaging.CreateBitmapSourceFromHIcon(
				icon.Handle,
				Int32Rect.Empty,
				BitmapSizeOptions.FromEmptyOptions());

			return imageSource;

			// Alternativ - vergleichen

			//using (var stream = new MemoryStream())
			//{
			//	icon.ToBitmap().Save(stream, ImageFormat.Png);
			//	var bitmapImage = new BitmapImage();
			//	bitmapImage.BeginInit();
			//	bitmapImage.StreamSource = new MemoryStream(stream.ToArray());
			//	bitmapImage.EndInit();
			//	bitmapImage.Freeze();

			//	return bitmapImage;
			//}
		}

		public ImageSource Icon => _iconSource ??= IconToImageSource((Window as WindowsWindow).ExtractIcon());

		public IWindow Window
		{
			get => _window;
			set
			{
				_window = value;

				RaisePropertyChanged();
				RaisePropertyChanged(nameof(Title));
				RaisePropertyChanged(nameof(Handle));

				ForceUpdatePreview();
			}
		}

		internal void ForceUpdatePreview()
		{
			Image = ImageSourceFromBitmap(Screenshot.CaptureWindow(Handle));
		}

		public IntPtr Handle => _window?.Handle ?? IntPtr.Zero;
	}
}
