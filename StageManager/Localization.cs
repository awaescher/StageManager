using StageManager.Core;
using System;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;

namespace StageManager
{
	/// <summary>The texts of the user interface as a binding source: they change right away when the language does.</summary>
	public sealed class LocSource : INotifyPropertyChanged
	{
		public static LocSource Instance { get; } = new();

		private LocSource()
		{
			Loc.LanguageChanged += (_, _) =>
			{
				void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

				// the settings can be changed from any thread, bindings only update on the UI thread
				var dispatcher = System.Windows.Application.Current?.Dispatcher;
				if (dispatcher is null || dispatcher.CheckAccess())
					Notify();
				else
					dispatcher.BeginInvoke(Notify);
			};
		}

		public event PropertyChangedEventHandler? PropertyChanged;

		public string this[string key] => Loc.Get(key);
	}

	/// <summary>Use as <c>Text="{local:Loc Settings.Title}"</c>.</summary>
	[MarkupExtensionReturnType(typeof(string))]
	public sealed class LocExtension : MarkupExtension
	{
		public LocExtension(string key)
		{
			Key = key;
		}

		public string Key { get; set; }

		public override object ProvideValue(IServiceProvider serviceProvider)
		{
			var binding = new Binding($"[{Key}]") { Source = LocSource.Instance, Mode = BindingMode.OneWay };
			return binding.ProvideValue(serviceProvider);
		}
	}
}
