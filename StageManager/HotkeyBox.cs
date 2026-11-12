using StageManager.Core;
using StageManager.Native;
using StageManager.Native.PInvoke;
using System;
using System.Windows.Controls;
using System.Windows.Input;

namespace StageManager
{
	/// <summary>
	/// A box to click and then hold the keys: Ctrl, Alt and Win in any mix. What was held at most at once is taken when all keys are let go.
	/// </summary>
	public class HotkeyBox : TextBox
	{
		private HotkeyModifiers _keys = HotkeyText.Default;
		private HotkeyModifiers _peak;
		private bool _capturing;

		public HotkeyBox()
		{
			// an implicit style is found by the exact type, so a box of its own type asks for the one of the theme for TextBox
			SetResourceReference(StyleProperty, typeof(TextBox));
			IsReadOnly = true;
			IsReadOnlyCaretVisible = false;
			IsUndoEnabled = false;
			ContextMenu = null;
			Padding = new System.Windows.Thickness(6, 3, 6, 3);
			Margin = new System.Windows.Thickness(0, 3, 0, 3);
			Cursor = Cursors.Hand;
			ShowKeys();

			Loc.LanguageChanged += (_, _) => Dispatcher.BeginInvoke(() =>
			{
				if (!_capturing)
					ShowKeys();
			});
		}

		/// <summary>The user chose new keys.</summary>
		public event EventHandler<HotkeyModifiers>? KeysChosen;

		public event EventHandler? CaptureStarted;

		public event EventHandler? CaptureEnded;

		public HotkeyModifiers Keys
		{
			get => _keys;
			set
			{
				_keys = value;

				if (!_capturing)
					ShowKeys();
			}
		}

		private void ShowKeys() => Text = HotkeyText.Format(_keys, Loc.KeyName);

		protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
		{
			base.OnGotKeyboardFocus(e);

			_capturing = true;
			_peak = HotkeyModifiers.None;
			Text = Loc.Get("Keyboard.Capture");
			CaptureStarted?.Invoke(this, EventArgs.Empty);
		}

		protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
		{
			base.OnLostKeyboardFocus(e);

			if (!_capturing)
				return;

			_capturing = false;
			ShowKeys();
			CaptureEnded?.Invoke(this, EventArgs.Empty);
		}

		protected override void OnPreviewKeyDown(KeyEventArgs e)
		{
			e.Handled = true;

			var key = e.Key == Key.System ? e.SystemKey : e.Key;

			switch (key)
			{
				case Key.Escape:
					Keyboard.ClearFocus();
					return;
				case Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift:
					break;
				case Key.LWin or Key.RWin:
					// a Win key let go alone opens the start menu, a key in between keeps it closed
					Win32.keybd_event(0xE8, 0, 0, UIntPtr.Zero);
					Win32.keybd_event(0xE8, 0, 2, UIntPtr.Zero);
					break;
				default:
					Text = Loc.Format("Keyboard.OnlyModifiers", Loc.KeyName("Ctrl"), Loc.KeyName("Alt"), Loc.KeyName("Win"), Loc.KeyName("Tab"));
					return;
			}

			Track();
		}

		protected override void OnPreviewKeyUp(KeyEventArgs e)
		{
			e.Handled = true;

			Track();
		}

		private void Track()
		{
			// Shift is not offered: Shift+Tab goes back
			var held = HeldKeys.Read() & ~HotkeyModifiers.Shift;

			if (held != HotkeyModifiers.None)
			{
				if (Count(held) >= Count(_peak))
					_peak = held;

				Text = HotkeyText.Format(held, Loc.KeyName) + "...";
				return;
			}

			if (!HotkeyText.IsUsable(_peak))
				return;

			var chosen = _peak;
			_peak = HotkeyModifiers.None;
			_keys = chosen;
			Keyboard.ClearFocus();
			KeysChosen?.Invoke(this, chosen);
		}

		private static int Count(HotkeyModifiers keys) => System.Numerics.BitOperations.PopCount((uint)keys);
	}
}
