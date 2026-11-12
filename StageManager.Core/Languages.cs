namespace StageManager.Core;

public sealed record AppLanguage(string Code, string NativeName);

/// <summary>The languages of the user interface. The texts themselves are resources of the app, this only knows which exist.</summary>
public static class Languages
{
	/// <summary>The language of the texts that are used when nothing else matches.</summary>
	public const string Neutral = "en";

	/// <summary>Written names, so that everybody finds their language even if the interface is in another one.</summary>
	public static IReadOnlyList<AppLanguage> All { get; } = new AppLanguage[]
	{
		new("en", "English"),
		new("de", "Deutsch"),
		new("es", "Español"),
		new("fr", "Français"),
		new("it", "Italiano"),
		new("pt", "Português"),
		new("ru", "Русский"),
		new("ja", "日本語"),
		new("ko", "한국어"),
		new("zh-Hans", "简体中文"),
		new("tr", "Türkçe")
	};

	public static bool IsSupported(string? code) =>
		!string.IsNullOrWhiteSpace(code) && All.Any(l => string.Equals(l.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

	/// <summary>The language setting as it is stored: a code of <see cref="All"/>, or empty to follow Windows.</summary>
	public static string Normalize(string? setting)
	{
		if (!IsSupported(setting))
			return "";

		return All.First(l => string.Equals(l.Code, setting!.Trim(), StringComparison.OrdinalIgnoreCase)).Code;
	}

	/// <summary>The language that fits a culture like "de-AT" or "zh-CN" best, <see cref="Neutral"/> if there is none.</summary>
	public static string Match(string? cultureName)
	{
		if (string.IsNullOrWhiteSpace(cultureName))
			return Neutral;

		var name = cultureName.Trim();

		var exact = All.FirstOrDefault(l => string.Equals(l.Code, name, StringComparison.OrdinalIgnoreCase));
		if (exact is not null)
			return exact.Code;

		var prefix = name.Split('-', '_')[0];

		// Chinese has two scripts: the traditional one has no texts yet
		if (string.Equals(prefix, "zh", StringComparison.OrdinalIgnoreCase))
		{
			var traditional = name.Contains("Hant", StringComparison.OrdinalIgnoreCase)
				|| name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase)
				|| name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase)
				|| name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase);

			return traditional ? Neutral : "zh-Hans";
		}

		var byPrefix = All.FirstOrDefault(l => string.Equals(l.Code, prefix, StringComparison.OrdinalIgnoreCase));
		return byPrefix?.Code ?? Neutral;
	}

	/// <param name="setting">The stored setting, empty to follow Windows.</param>
	/// <param name="systemCulture">The display language of Windows.</param>
	public static string Resolve(string? setting, string? systemCulture) =>
		IsSupported(setting) ? Normalize(setting) : Match(systemCulture);
}
