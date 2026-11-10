using StageManager.Core;
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

			// an icon is a Windows UI object, of which a process gets only 10000: the picture is copied, the icon has to go
			using (icon)
			{
				var imageSource = Imaging.CreateBitmapSourceFromHIcon(
					icon.Handle,
					Int32Rect.Empty,
					BitmapSizeOptions.FromEmptyOptions());

				imageSource.Freeze();
				return imageSource;
			}
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
			}
		}

		public IntPtr Handle => _window?.Handle ?? IntPtr.Zero;

		public double Width { get; private set; } = 150;

		public double Height { get; private set; } = 100;

		public Thickness Margin { get; private set; }

		/// <summary>
		/// Windows beyond the visible stack are only shown as icons, so their thumbnail is not registered at DWM.
		/// </summary>
		public bool IsThumbnailVisible { get; private set; } = true;

		public void ApplySlot(StackSlot slot)
		{
			Width = slot.Width;
			Height = slot.Height;
			// the slot offsets are measured from the bottom left corner of the card
			Margin = new Thickness(slot.OffsetX, 0, 0, slot.OffsetY);
			IsThumbnailVisible = slot.IsVisible;

			RaisePropertyChanged(nameof(Width));
			RaisePropertyChanged(nameof(Height));
			RaisePropertyChanged(nameof(Margin));
			RaisePropertyChanged(nameof(IsThumbnailVisible));
		}
	}
}
