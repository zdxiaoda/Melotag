namespace Melotag.UI.Dialogs;

using Avalonia.Controls;
using Avalonia.Interactivity;

/// <summary>通用信息浮层：关于、提示等只需要一段内容的场景。</summary>
public partial class InfoDialog : ModalView
{
    public InfoDialog()
    {
        InitializeComponent();
    }

    public InfoDialog(string title, Control content)
    {
        InitializeComponent();
        TitleText.Text = title;
        Body.Content = content;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(true);
}
