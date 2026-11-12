using StageManager.Core;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace StageManager.Tests;

public class LocalizationTests
{
	private static string ResourceDirectory
	{
		get
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StageManager.sln")))
				dir = dir.Parent;

			return Path.Combine(dir!.FullName, "StageManager.Core", "Resources");
		}
	}

	private static Dictionary<string, string> Read(string language)
	{
		var file = language == "en" ? "Strings.resx" : $"Strings.{language}.resx";
		var doc = XDocument.Load(Path.Combine(ResourceDirectory, file));

		return doc.Root!.Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);
	}

	public static IEnumerable<object[]> OtherLanguages() =>
		Languages.All.Where(l => l.Code != Languages.Neutral).Select(l => new object[] { l.Code });

	public static IEnumerable<object[]> AllLanguages() =>
		Languages.All.Select(l => new object[] { l.Code });

	[Theory]
	[MemberData(nameof(OtherLanguages))]
	public void Every_language_has_exactly_the_keys_of_English(string language)
	{
		var english = Read("en");
		var other = Read(language);

		Assert.Empty(english.Keys.Except(other.Keys));
		Assert.Empty(other.Keys.Except(english.Keys));
	}

	[Theory]
	[MemberData(nameof(OtherLanguages))]
	public void Every_language_uses_the_same_placeholders_as_English(string language)
	{
		var english = Read("en");
		var other = Read(language);

		static string Placeholders(string text) =>
			string.Join(",", Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Distinct().OrderBy(v => v));

		foreach (var (key, text) in english)
			Assert.True(Placeholders(text) == Placeholders(other[key]), $"{language}: placeholders of {key} differ");
	}

	[Theory]
	[MemberData(nameof(AllLanguages))]
	public void No_text_is_empty(string language)
	{
		foreach (var (key, text) in Read(language))
			Assert.False(string.IsNullOrWhiteSpace(text), $"{language}: {key} is empty");
	}

	[Theory]
	[MemberData(nameof(OtherLanguages))]
	public void Texts_are_translated_not_copied(string language)
	{
		var english = Read("en");
		var other = Read(language);

		// names of keys and a few words are the same in many languages
		var copied = english.Where(e => e.Value == other[e.Key] && e.Value.Length > 25).Select(e => e.Key).ToList();

		Assert.True(copied.Count == 0, $"{language}: not translated: {string.Join(", ", copied)}");
	}

	[Fact]
	public void All_keys_are_in_the_resource_assembly()
	{
		foreach (var language in Languages.All)
		{
			foreach (var key in Read("en").Keys)
				Assert.False(string.IsNullOrEmpty(Loc.GetIn(language.Code, key)), $"{language.Code}: {key}");
		}
	}

	[Fact]
	public void The_languages_are_the_ten_most_common_ones_plus_English()
	{
		Assert.Equal(11, Languages.All.Count);
		Assert.Equal(Languages.All.Count, Languages.All.Select(l => l.Code).Distinct().Count());
		Assert.All(Languages.All, l => Assert.False(string.IsNullOrWhiteSpace(l.NativeName)));
	}

	[Theory]
	[InlineData("de-DE", "de")]
	[InlineData("de-AT", "de")]
	[InlineData("DE", "de")]
	[InlineData("en-US", "en")]
	[InlineData("en-GB", "en")]
	[InlineData("pt-BR", "pt")]
	[InlineData("pt-PT", "pt")]
	[InlineData("es-MX", "es")]
	[InlineData("fr-CA", "fr")]
	[InlineData("zh-CN", "zh-Hans")]
	[InlineData("zh-Hans-CN", "zh-Hans")]
	[InlineData("zh", "zh-Hans")]
	[InlineData("zh-TW", "en")]
	[InlineData("zh-Hant", "en")]
	[InlineData("zh-HK", "en")]
	[InlineData("ja-JP", "ja")]
	[InlineData("ko-KR", "ko")]
	[InlineData("tr-TR", "tr")]
	[InlineData("ru-RU", "ru")]
	[InlineData("it-IT", "it")]
	[InlineData("sv-SE", "en")]
	[InlineData("", "en")]
	[InlineData(null, "en")]
	public void A_culture_finds_the_language_that_fits(string? culture, string expected)
	{
		Assert.Equal(expected, Languages.Match(culture));
	}

	[Theory]
	[InlineData("", "de-DE", "de")]
	[InlineData("fr", "de-DE", "fr")]
	[InlineData("FR", "de-DE", "fr")]
	[InlineData("xx", "ja-JP", "ja")]
	[InlineData("  ", "ko-KR", "ko")]
	[InlineData("", "", "en")]
	public void The_setting_wins_over_Windows(string setting, string system, string expected)
	{
		Assert.Equal(expected, Languages.Resolve(setting, system));
	}

	[Theory]
	[InlineData(null, "")]
	[InlineData("", "")]
	[InlineData("de", "de")]
	[InlineData(" DE ", "de")]
	[InlineData("zh-hans", "zh-Hans")]
	[InlineData("klingon", "")]
	public void The_language_in_the_settings_is_a_known_code_or_empty(string? setting, string expected)
	{
		var s = new AppSettings { Language = setting! }.Normalize();

		Assert.Equal(expected, s.Language);
	}

	[Fact]
	public void The_language_defaults_to_Windows()
	{
		Assert.Equal("", new AppSettings().Normalize().Language);
	}

	[Fact]
	public void A_missing_text_shows_its_key_instead_of_nothing()
	{
		Assert.Equal("!No.Such.Key!", Loc.Get("No.Such.Key"));
	}

	[Fact]
	public void Texts_follow_the_language_and_keys_are_formatted()
	{
		var before = Loc.Language;
		try
		{
			Loc.SetLanguage("de");
			Assert.Equal("Schließen", Loc.Get("Card.Close"));
			Assert.Equal("Strg", Loc.KeyName("Ctrl"));
			Assert.Contains("3", Loc.Format("Sidebar.Limited", 3, 6));

			Loc.SetLanguage("en");
			Assert.Equal("Close", Loc.Get("Card.Close"));
		}
		finally
		{
			Loc.SetLanguage(before);
		}
	}

	[Fact]
	public void The_language_follows_the_settings_from_start_and_on_every_change()
	{
		var before = Loc.Language;
		var dir = Path.Combine(Path.GetTempPath(), "StageManagerTests", Guid.NewGuid().ToString("N"));

		try
		{
			var service = new SettingsService(new SettingsStore(Path.Combine(dir, "settings.json")));
			service.Update(s => s.Language = "ja");

			Loc.Follow(service);
			Assert.Equal("ja", Loc.Language);

			service.Update(s => s.Language = "de");
			Assert.Equal("de", Loc.Language);
			Assert.Equal("Schließen", Loc.Get("Card.Close"));

			service.Update(s => s.Language = "xx");
			Assert.Equal(Languages.Match(Loc.SystemCulture), Loc.Language);

			service.Reset();
			Assert.Equal(Languages.Match(Loc.SystemCulture), Loc.Language);
		}
		finally
		{
			Loc.SetLanguage(before);

			if (Directory.Exists(dir))
				Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void Changing_the_language_is_announced_once()
	{
		var before = Loc.Language;
		var raised = 0;
		EventHandler handler = (_, _) => raised++;
		Loc.LanguageChanged += handler;

		try
		{
			Loc.SetLanguage("fr");
			Loc.SetLanguage("fr");
			Loc.SetLanguage("it");

			Assert.Equal(2, raised);
		}
		finally
		{
			Loc.LanguageChanged -= handler;
			Loc.SetLanguage(before);
		}
	}

	[Fact]
	public void Every_owner_and_warning_has_a_text()
	{
		foreach (var owner in Enum.GetValues<HotkeyOwner>())
			Assert.False(string.IsNullOrEmpty(Loc.GetIn("en", "Owner." + owner)), owner.ToString());

		foreach (var warning in Enum.GetValues<HotkeyWarning>().Where(w => w != HotkeyWarning.None))
			Assert.False(string.IsNullOrEmpty(Loc.GetIn("en", "Warn." + warning)), warning.ToString());
	}

	[Fact]
	public void Culture_names_of_the_languages_exist_in_the_runtime()
	{
		foreach (var language in Languages.All)
			Assert.Equal(language.Code, CultureInfo.GetCultureInfo(language.Code).Name);
	}
}
