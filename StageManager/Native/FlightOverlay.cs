using StageManager.Core;
using StageManager.Native.Interop;
using StageManager.Native.PInvoke;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace StageManager.Native
{
	/// <summary>
	/// A transparent window over a whole display that lets every click through. The windows of a scene fly on it between the
	/// sidebar and the stage: either as DWM thumbnails (which DWM draws from the live or last picture of the window) or as frames.
	/// </summary>
	public sealed class FlightOverlay : System.Windows.Window
	{
		private sealed class Item
		{
			public required Pose From { get; init; }
			public required Pose To { get; init; }
			public IntPtr Thumbnail { get; set; }
			public Border? Frame { get; set; }
			public Flight? Flight { get; set; }
		}

		private readonly Area _bounds;
		private readonly FlightStyle _style;
		private readonly Canvas _canvas = new();
		private readonly List<Item> _items = new();
		private readonly Stopwatch _clock = Stopwatch.StartNew();
		private IntPtr _handle;
		private double _dpi = 1;
		private TaskCompletionSource? _flying;
		private bool _skip;

		public FlightOverlay(Area monitorBounds, FlightStyle style)
		{
			_bounds = monitorBounds;
			_style = style;

			WindowStyle = System.Windows.WindowStyle.None;
			AllowsTransparency = true;
			Background = Brushes.Transparent;
			ShowInTaskbar = false;
			ShowActivated = false;
			Topmost = true;
			ResizeMode = System.Windows.ResizeMode.NoResize;
			Content = _canvas;
			Left = monitorBounds.Left;
			Top = monitorBounds.Top;
			Width = monitorBounds.Width;
			Height = monitorBounds.Height;

			SourceInitialized += (_, _) =>
			{
				_handle = new WindowInteropHelper(this).Handle;
				Win32.SetWindowStyleExLongPtr(_handle, Win32.GetWindowExStyleLongPtr(_handle)
					| Win32.WS_EX.WS_EX_TRANSPARENT | Win32.WS_EX.WS_EX_TOOLWINDOW | Win32.WS_EX.WS_EX_NOACTIVATE);
				NativeMethods.SetTransitionsDisabled(_handle, true);
				Place();
			};
			ContentRendered += (_, _) => Place();
		}

		/// <summary>Sets the exact pixel rectangle, the sizes WPF works out from device independent units can be off by a pixel.</summary>
		private void Place()
		{
			_dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
			Win32.SetWindowPos(_handle, Win32.HWND_TOPMOST, (int)_bounds.Left, (int)_bounds.Top, (int)_bounds.Width, (int)_bounds.Height,
				Win32.SetWindowPosFlags.DoNotActivate);
		}

		/// <summary>Prepares a window at its starting pose. Nothing moves until <see cref="Fly"/>.</summary>
		public void Add(IntPtr window, Pose from, Pose to)
		{
			var item = new Item { From = from, To = to };

			if (_style == FlightStyle.Windows)
			{
				if (NativeMethods.DwmRegisterThumbnail(_handle, window, out var thumbnail) != 0)
					return;

				item.Thumbnail = thumbnail;
			}
			else
			{
				// accent colored with a shadow, so it shows on light and dark backgrounds alike
				var accent = SystemParameters.WindowGlassColor;
				item.Frame = new Border
				{
					CornerRadius = new CornerRadius(8),
					BorderThickness = new Thickness(3),
					BorderBrush = new SolidColorBrush(Color.FromArgb(235, accent.R, accent.G, accent.B)),
					Background = new SolidColorBrush(Color.FromArgb(48, accent.R, accent.G, accent.B)),
					Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black }
				};
				_canvas.Children.Add(item.Frame);
			}

			_items.Add(item);
			Apply(item, from);
		}

		public bool HasFlights => _items.Count > 0;

		public Task Fly(TimeSpan duration)
		{
			_flying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			var start = _clock.Elapsed.TotalSeconds;
			foreach (var item in _items)
				item.Flight = new Flight(0, item.From, item.To, duration, start);

			CompositionTarget.Rendering += OnRendering;
			return _flying.Task;
		}

		/// <summary>Lets all flights land right away. The awaiting <see cref="Fly"/> completes with the next frame.</summary>
		public void Skip() => _skip = true;

		private void OnRendering(object? sender, EventArgs e)
		{
			var now = _skip ? double.MaxValue / 4 : _clock.Elapsed.TotalSeconds;

			foreach (var item in _items)
				Apply(item, item.Flight!.At(now));

			if (_items.All(i => i.Flight!.IsDone(now)))
			{
				CompositionTarget.Rendering -= OnRendering;
				_flying?.TrySetResult();
			}
		}

		private void Apply(Item item, Pose pose)
		{
			var left = pose.X - _bounds.Left;
			var top = pose.Y - _bounds.Top;

			if (item.Thumbnail != IntPtr.Zero)
			{
				var properties = new DWM_THUMBNAIL_PROPERTIES
				{
					fVisible = true,
					dwFlags = (int)(DWM_TNP.DWM_TNP_VISIBLE | DWM_TNP.DWM_TNP_OPACITY | DWM_TNP.DWM_TNP_RECTDESTINATION),
					opacity = (byte)Math.Clamp(Math.Round(255 * pose.Opacity), 0, 255),
					rcDestination = new RECT
					{
						left = (int)Math.Round(left),
						top = (int)Math.Round(top),
						right = (int)Math.Round(left + pose.Width),
						bottom = (int)Math.Round(top + pose.Height)
					}
				};
				NativeMethods.DwmUpdateThumbnailProperties(item.Thumbnail, ref properties);
			}
			else if (item.Frame is not null)
			{
				Canvas.SetLeft(item.Frame, left / _dpi);
				Canvas.SetTop(item.Frame, top / _dpi);
				item.Frame.Width = Math.Max(0, pose.Width / _dpi);
				item.Frame.Height = Math.Max(0, pose.Height / _dpi);
				item.Frame.Opacity = pose.Opacity;
			}
		}

		/// <summary>Removes the flights and closes the window.</summary>
		public void Finish()
		{
			CompositionTarget.Rendering -= OnRendering;

			foreach (var item in _items.Where(i => i.Thumbnail != IntPtr.Zero))
				NativeMethods.DwmUnregisterThumbnail(item.Thumbnail);

			_items.Clear();
			_flying?.TrySetResult();
			Close();
		}
	}
}
