using StageManager.Core;
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
		private FittedRect _backdrop;

		public static readonly DependencyProperty PreviewHandleProperty = DependencyProperty.Register(nameof(PreviewHandle),
			   typeof(IntPtr),
			   typeof(DwmThumbnail),
			   new PropertyMetadata(IntPtr.Zero));

		public IntPtr PreviewHandle
		{
			get { return (IntPtr)GetValue(PreviewHandleProperty); }
			set { SetValue(PreviewHandleProperty, value); }
		}

		/// <summary>
		/// Opacity of the thumbnails below an element. DWM draws thumbnails above all WPF content, so the opacity of WPF does not reach them.
		/// </summary>
		public static readonly DependencyProperty ThumbnailOpacityProperty = DependencyProperty.RegisterAttached("ThumbnailOpacity",
			typeof(double),
			typeof(DwmThumbnail),
			new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.Inherits));

		public static double GetThumbnailOpacity(DependencyObject element) => (double)element.GetValue(ThumbnailOpacityProperty);

		public static void SetThumbnailOpacity(DependencyObject element, double value) => element.SetValue(ThumbnailOpacityProperty, value);

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

			if (e.Property == PreviewHandleProperty)
			{
				if ((IntPtr)e.OldValue == IntPtr.Zero && (IntPtr)e.NewValue != IntPtr.Zero)
					StartCapture();

				UpdateThumbnailProperties();
			}

			if (e.Property == ThumbnailOpacityProperty)
				UpdateThumbnailProperties();

			if (e.Property == IsVisibleProperty)
			{
				if (!(bool)e.NewValue)
				{
					StopCapture();
				}
				else if (_dwmThumbnail == IntPtr.Zero && PreviewHandle != IntPtr.Zero)
				{
					// scenes and stacked windows are hidden and shown again, the thumbnail has to be registered again
					StartCapture();
					UpdateThumbnailProperties();
				}
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
			var window = FindWindow();
			if (window is null)
				return;

			var windowHandle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
			if (windowHandle == IntPtr.Zero)
				return;

			var hr = NativeMethods.DwmRegisterThumbnail(windowHandle, PreviewHandle, out _dwmThumbnail);
			if (hr != 0)
				_dwmThumbnail = IntPtr.Zero;
		}

		private void StopCapture()
		{
			if (_dwmThumbnail == IntPtr.Zero)
				return;

			NativeMethods.DwmUnregisterThumbnail(_dwmThumbnail);
			_dwmThumbnail = IntPtr.Zero;
		}

		private Window FindWindow() => _window ??= Window.GetWindow(this);

		private (double Width, double Height) _lastSourceSize;

		/// <summary>
		/// Size of the area DWM shows, in the unit of the source window. Only the proportions matter here.
		/// </summary>
		private (double Width, double Height) GetSourceSize()
		{
			// DWM knows the size of the last rendered frame, also for minimized windows
			if (_dwmThumbnail != IntPtr.Zero && NativeMethods.DwmQueryThumbnailSourceSize(_dwmThumbnail, out var size) == 0 && size.cx > 0 && size.cy > 0)
				return _lastSourceSize = (size.cx, size.cy);

			// never ask a minimized window for its size, Chromium reports the 144 x 20 pixels of its minimized caption bar
			if (!NativeMethods.IsIconic(PreviewHandle))
			{
				if (NativeMethods.GetClientRect(PreviewHandle, out var client) && client.right > 0 && client.bottom > 0)
					return _lastSourceSize = (client.right, client.bottom);
			}
			else if (NativeMethods.TryGetRestoredSize(PreviewHandle, out var restored))
			{
				return _lastSourceSize = restored;
			}

			return _lastSourceSize;
		}

		private void UpdateBackdrop(FittedRect fit)
		{
			if (fit == _backdrop)
				return;

			_backdrop = fit;
			backdrop.Margin = new Thickness(fit.X, fit.Y, 0, 0);
			backdrop.Width = fit.Width;
			backdrop.Height = fit.Height;
		}

		private void UpdateThumbnailProperties()
		{
			if (_dwmThumbnail == IntPtr.Zero)
				return;

			var window = FindWindow();
			if (window is null || ActualWidth <= 0 || ActualHeight <= 0)
				return;

			var dpi = GetDpiScaleFactor();
			var origin = this.TransformToVisual(window).Transform(new Point(0, 0));

			// the white backdrop and the thumbnail share the same proportions as the source window, so it is not distorted
			var source = GetSourceSize();
			var fit = AspectFit.Calculate(ActualWidth, ActualHeight, source.Width, source.Height);
			UpdateBackdrop(fit);

			var thumbnailRect = new RECT
			{
				left = (int)Math.Round((origin.X + fit.X) * dpi.X),
				top = (int)Math.Round((origin.Y + fit.Y) * dpi.Y),
				right = (int)Math.Round((origin.X + fit.X + fit.Width) * dpi.X),
				bottom = (int)Math.Round((origin.Y + fit.Y + fit.Height) * dpi.Y)
			};

			var props = new DWM_THUMBNAIL_PROPERTIES
			{
				fVisible = true,
				dwFlags = (int)(DWM_TNP.DWM_TNP_VISIBLE | DWM_TNP.DWM_TNP_OPACITY | DWM_TNP.DWM_TNP_RECTDESTINATION | DWM_TNP.DWM_TNP_SOURCECLIENTAREAONLY),
				opacity = (byte)Math.Clamp(Math.Round(255 * GetThumbnailOpacity(this)), 0, 255),
				rcDestination = thumbnailRect,
				fSourceClientAreaOnly = true
			};
			NativeMethods.DwmUpdateThumbnailProperties(_dwmThumbnail, ref props);
		}
	}
}
