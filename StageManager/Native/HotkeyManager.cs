using StageManager.Core;
using StageManager.Native.PInvoke;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace StageManager.Native
{
	/// <summary>Which of the modifier keys are down right now.</summary>
	public static class HeldKeys
	{
		private const int VK_SHIFT = 0x10;
		private const int VK_CONTROL = 0x11;
		private const int VK_MENU = 0x12;
		private const int VK_LWIN = 0x5B;
		private const int VK_RWIN = 0x5C;

		public static HotkeyModifiers Read()
		{
			var keys = HotkeyModifiers.None;

			if (IsDown(VK_CONTROL))
				keys |= HotkeyModifiers.Ctrl;
			if (IsDown(VK_MENU))
				keys |= HotkeyModifiers.Alt;
			if (IsDown(VK_SHIFT))
				keys |= HotkeyModifiers.Shift;
			if (IsDown(VK_LWIN) || IsDown(VK_RWIN))
				keys |= HotkeyModifiers.Win;

			return keys;
		}

		/// <summary>True if all of the given modifiers are down.</summary>
		public static bool AreDown(HotkeyModifiers modifiers) => (Read() & modifiers) == modifiers;

		public static bool IsEscapeDown() => IsDown(0x1B);

		private static bool IsDown(int vk) => (Win32.GetAsyncKeyState(vk) & 0x8000) != 0;
	}

	/// <summary>
	/// Registers the keys that switch scenes with Windows. The hotkeys of Windows need no keyboard hook, so they cannot be removed
	/// for answering too slowly and they do not touch the keys of other apps.
	/// </summary>
	public sealed class HotkeyManager : IDisposable
	{
		private const int WM_HOTKEY = 0x0312;
		private const uint VK_TAB = 0x09;
		private const uint MOD_SHIFT = 0x0004;
		private const uint MOD_NOREPEAT = 0x4000;
		private const int IdNext = 1;
		private const int IdPrevious = 2;
		private const int IdCardBase = 10;

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

		private readonly HwndSource _source;
		private readonly List<int> _registered = new();
		private bool _enabled;
		private bool _suspended;
		private HotkeyModifiers _keys = HotkeyText.Default;

		public HotkeyManager()
		{
			// a window that only receives messages: no taskbar button, no focus, nothing to see
			var parameters = new HwndSourceParameters("StageManager hotkeys") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 };
			_source = new HwndSource(parameters);
			_source.AddHook(WndProc);
		}

		public event EventHandler<HotkeyAction>? Triggered;

		/// <summary>Raised when the lines of <see cref="Conflicts"/> changed.</summary>
		public event EventHandler? ConflictsChanged;

		/// <summary>One line for each combination that could not be taken, empty if all work.</summary>
		public IReadOnlyList<string> Conflicts { get; private set; } = Array.Empty<string>();

		/// <summary>While the user records new keys the registered ones must not swallow them.</summary>
		public bool Suspended
		{
			get => _suspended;
			set
			{
				if (_suspended == value)
					return;

				_suspended = value;
				Register();
			}
		}

		public void Apply(bool enabled, HotkeyModifiers keys)
		{
			_enabled = enabled;
			_keys = keys;
			Register();
		}

		private void Register()
		{
			Unregister();

			var failed = new List<HotkeyAction>();

			if (_enabled && !_suspended && HotkeyText.IsUsable(_keys))
			{
				foreach (var action in HotkeyAction.All())
				{
					var (id, key, extra, repeat) = action.Kind switch
					{
						HotkeyActionKind.Next => (IdNext, VK_TAB, 0u, true),
						HotkeyActionKind.Previous => (IdPrevious, VK_TAB, MOD_SHIFT, true),
						_ => (IdCardBase + action.Number, (uint)('0' + action.Number), 0u, false)
					};

					var modifiers = (uint)_keys | extra | (repeat ? 0 : MOD_NOREPEAT);

					if (RegisterHotKey(_source.Handle, id, modifiers, key))
						_registered.Add(id);
					else
						failed.Add(action);
				}
			}

			var conflicts = HotkeyAdvice.Conflicts(_keys, failed);
			if (!conflicts.SequenceEqual(Conflicts))
			{
				Conflicts = conflicts;
				ConflictsChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		private void Unregister()
		{
			foreach (var id in _registered)
				UnregisterHotKey(_source.Handle, id);

			_registered.Clear();
		}

		private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if (msg != WM_HOTKEY)
				return IntPtr.Zero;

			var id = wParam.ToInt32();
			HotkeyAction? action = id switch
			{
				IdNext => HotkeyAction.Next,
				IdPrevious => HotkeyAction.Previous,
				>= IdCardBase + 1 and <= IdCardBase + HotkeyText.MaxNumber => HotkeyAction.Card(id - IdCardBase),
				_ => null
			};

			if (action is HotkeyAction a)
			{
				handled = true;
				Triggered?.Invoke(this, a);
			}

			return IntPtr.Zero;
		}

		public void Dispose()
		{
			Unregister();
			_source.RemoveHook(WndProc);
			_source.Dispose();
		}
	}
}
