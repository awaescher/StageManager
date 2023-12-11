using StageManager.Native.Interop;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StageManager
{
	/// <summary>
	/// Interaction logic for DwmThumbnail.xaml
	/// </summary>
	public partial class DwmThumbnail : UserControl
	{
		public DwmThumbnail()
		{
			InitializeComponent();
			LayoutUpdated += DwmThumbnail_LayoutUpdated;
		}

		private IntPtr _dwmThumbnail;
		private Window _window;
		private Point? _dpiScaleFactor;

		public static readonly DependencyProperty PreviewHandleProperty = DependencyProperty.Register(nameof(PreviewHandle),
			   typeof(IntPtr),
			   typeof(DwmThumbnail),
			   new PropertyMetadata(IntPtr.Zero));

		public IntPtr PreviewHandle
		{
			get { return (IntPtr)GetValue(PreviewHandleProperty); }
			set { SetValue(PreviewHandleProperty, value); }
		}

		private Point GetDpiScaleFactor()
		{
			if (_dpiScaleFactor is null)
			{
				var source = PresentationSource.FromVisual(this);
				_dpiScaleFactor = source?.CompositionTarget != null ? new Point(source.CompositionTarget.TransformToDevice.M11, source.CompositionTarget.TransformToDevice.M22) : new Point(1.0d, 1.0d);
			}

			return _dpiScaleFactor.Value;
		}

		protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
		{
			_dpiScaleFactor = null;
			base.OnDpiChanged(oldDpi, newDpi);
		}

		protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
		{
			base.OnPropertyChanged(e);

			if (nameof(PreviewHandle).Equals(e.Property.Name))
			{
				if ((IntPtr)e.OldValue == IntPtr.Zero && (IntPtr)e.NewValue != IntPtr.Zero)
					StartCapture();

				UpdateThumbnailProperties();
			}

			if (nameof(IsVisible).Equals(e.Property.Name) && !(bool)e.NewValue && _dwmThumbnail != IntPtr.Zero)
			{
				NativeMethods.DwmUnregisterThumbnail(_dwmThumbnail);
				_dwmThumbnail = IntPtr.Zero;
			}
		}

		private void DwmThumbnail_LayoutUpdated(object? sender, EventArgs e)
		{
			UpdateThumbnailProperties();
		}

		public static Rect BoundsRelativeTo(FrameworkElement element, Visual relativeTo)
		{
			return element.TransformToVisual(relativeTo)
						  .TransformBounds(System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(element));
		}

		private void StartCapture()
		{
			var windowHandle = new System.Windows.Interop.WindowInteropHelper(FindWindow()).Handle;

			var hr = NativeMethods.DwmRegisterThumbnail(windowHandle, PreviewHandle, out _dwmThumbnail);
			if (hr != 0)
				return;
		}

		private Window FindWindow() => _window ??= Window.GetWindow(this);

		private void UpdateThumbnailProperties()
		{
			if (_dwmThumbnail == IntPtr.Zero)
				return;

			var dpi = GetDpiScaleFactor();

			var previewBounds = BoundsRelativeTo(this, FindWindow());

			var thumbnailRect = new RECT
			{
				top = (int)(previewBounds.Top * dpi.Y),
				left = (int)(previewBounds.Left * dpi.X),
				bottom = (int)((previewBounds.Bottom - Margin.Top - Margin.Bottom) * dpi.Y) + 1,
				right = (int)((previewBounds.Right - Margin.Left - Margin.Right) * dpi.X) + 1
			};

			var props = new DWM_THUMBNAIL_PROPERTIES
			{
				fVisible = true,
				dwFlags = (int)(DWM_TNP.DWM_TNP_VISIBLE | DWM_TNP.DWM_TNP_OPACITY | DWM_TNP.DWM_TNP_RECTDESTINATION | DWM_TNP.DWM_TNP_SOURCECLIENTAREAONLY),
				opacity = 255,
				rcDestination = thumbnailRect,
				fSourceClientAreaOnly = true
			};
			NativeMethods.DwmUpdateThumbnailProperties(_dwmThumbnail, ref props);
		}
	}
}
