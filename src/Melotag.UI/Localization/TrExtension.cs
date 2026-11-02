namespace Melotag.UI.Localization;

using Avalonia.Data;
using Avalonia.Markup.Xaml;

/// <summary>XAML 用法：<c>Text="{loc:Tr Key=Field.Title}"</c>。</summary>
public sealed class TrExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(LocalizedString.Value))
        {
            Source = Localizer.Instance.Get(Key),
            Mode = BindingMode.OneWay,
        };
}
