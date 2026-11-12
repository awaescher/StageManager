using System.Globalization;
using System.Resources;

namespace StageManager.Core;

/// <summary>The texts of the user interface, in the language the user chose. The texts are resources of this assembly (Resources/Strings*.resx).</summary>
public static class Loc
{
	private static readonly ResourceManager Manager = new("StageManager.Core.Resources.Strings", typeof(Loc).Assembly);

	private static CultureInfo _culture = CultureInfo.GetCultureInfo(Languages.Neutral);

	/// <summary>The display language of Windows, read once before the app changes its own.</summary>
	public static string SystemCulture { get; } = CultureInfo.CurrentUICulture.Name;

	/// <summary>The code of the language in use, one of <see cref="Languages.All"/>.</summary>
	public static string Language => _culture.Name;

	public static event EventHandler? LanguageChanged;

	/// <summary>Uses the language of the settings now and every time they change.</summary>
	public static void Follow(SettingsService settings)
	{
		SetLanguage(settings.Current.Language);
		settings.Changed += (_, current) => SetLanguage(current.Language);
	}

	/// <param name="setting">The language setting: a code like "de", or empty to follow Windows.</param>
	public static void SetLanguage(string? setting)
	{
		var code = Languages.Resolve(setting, SystemCulture);

		if (string.Equals(code, _culture.Name, StringComparison.OrdinalIgnoreCase))
			return;

		_culture = CultureInfo.GetCultureInfo(code);

		// also the texts Windows itself shows for the app, and everything created from now on
		CultureInfo.CurrentUICulture = _culture;
		CultureInfo.DefaultThreadCurrentUICulture = _culture;

		LanguageChanged?.Invoke(null, EventArgs.Empty);
	}

	/// <summary>The text for a key. A key without text shows up as !Key!, so that it is seen right away and nothing is blank.</summary>
	public static string Get(string key) => Manager.GetString(key, _culture) ?? $"!{key}!";

	public static string Format(string key, params object[] args) => string.Format(_culture, Get(key), args);

	/// <summary>The text for a key in a given language. Only for tests and tools.</summary>
	public static string? GetIn(string language, string key) => Manager.GetString(key, CultureInfo.GetCultureInfo(language));

	/// <summary>The name of a key of the keyboard ("Ctrl", "Tab"), as the user knows it.</summary>
	public static string KeyName(string invariantName) => Get("Key." + invariantName);
}
