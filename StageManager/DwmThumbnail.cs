using StageManager.Native.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace StageManager
{
	/// <summary>
	/// Follow steps 1a or 1b and then 2 to use this custom control in a XAML file.
	///
	/// Step 1a) Using this custom control in a XAML file that exists in the current project.
	/// Add this XmlNamespace attribute to the root element of the markup file where it is 
	/// to be used:
	///
	///     xmlns:MyNamespace="clr-namespace:StageManager"
	///
	///
	/// Step 1b) Using this custom control in a XAML file that exists in a different project.
	/// Add this XmlNamespace attribute to the root element of the markup file where it is 
	/// to be used:
	///
	///     xmlns:MyNamespace="clr-namespace:StageManager;assembly=StageManager"
	///
	/// You will also need to add a project reference from the project where the XAML file lives
	/// to this project and Rebuild to avoid compilation errors:
	///
	///     Right click on the target project in the Solution Explorer and
	///     "Add Reference"->"Projects"->[Browse to and select this project]
	///
	///
	/// Step 2)
	/// Go ahead and use your control in the XAML file.
	///
	///     <MyNamespace:DwmThumbnail/>
	///
	/// </summary>
	public class DwmThumbnail : Control
	{
		static DwmThumbnail()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(DwmThumbnail), new FrameworkPropertyMetadata(typeof(DwmThumbnail)));
		}

		private IntPtr _dwmThumbnail;

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
			var source = PresentationSource.FromVisual(this);
			return source?.CompositionTarget != null ? new Point(source.CompositionTarget.TransformToDevice.M11, source.CompositionTarget.TransformToDevice.M22) : new Point(1.0d, 1.0d);
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

		protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
		{
			base.OnRenderSizeChanged(sizeInfo);

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

		private Window FindWindow() => Window.GetWindow(this);

		private void UpdateThumbnailProperties()
		{
			if (_dwmThumbnail == IntPtr.Zero)
				return;

			var dpi = GetDpiScaleFactor();

			var previewBounds = BoundsRelativeTo(this, FindWindow());

			var props = new DWM_THUMBNAIL_PROPERTIES
			{
				fVisible = true,
				dwFlags = (int)(DWM_TNP.DWM_TNP_VISIBLE | DWM_TNP.DWM_TNP_OPACITY | DWM_TNP.DWM_TNP_RECTDESTINATION | DWM_TNP.DWM_TNP_SOURCECLIENTAREAONLY),
				opacity = 255,
				rcDestination = new RECT
				{
					top = (int)(previewBounds.Top * dpi.Y),
					left = (int)(previewBounds.Left * dpi.X),
					bottom = (int)(previewBounds.Bottom * dpi.Y),
					right = (int)(previewBounds.Right * dpi.X)
				},
				fSourceClientAreaOnly = true
			};

			NativeMethods.DwmUpdateThumbnailProperties(_dwmThumbnail, ref props);
		}
	}
}
