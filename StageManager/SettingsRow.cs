using System.Windows;
using System.Windows.Controls;

namespace StageManager
{
	/// <summary>One setting: a title and a description on the left, the control that changes it on the right.</summary>
	public class SettingsRow : ContentControl
	{
		public static readonly DependencyProperty TitleProperty =
			DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingsRow), new PropertyMetadata(""));

		public static readonly DependencyProperty DescriptionProperty =
			DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsRow), new PropertyMetadata(""));

		static SettingsRow()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingsRow), new FrameworkPropertyMetadata(typeof(SettingsRow)));
		}

		public string Title
		{
			get => (string)GetValue(TitleProperty);
			set => SetValue(TitleProperty, value);
		}

		public string Description
		{
			get => (string)GetValue(DescriptionProperty);
			set => SetValue(DescriptionProperty, value);
		}
	}
}
