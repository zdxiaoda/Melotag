namespace Melotag.UI.Dialogs;

using Avalonia.Interactivity;
using Melotag.UI.Localization;

public enum ExitChoice
{
    Cancel,
    SaveAndExit,
    ExitWithoutSave,
}

/// <summary>退出前的未保存确认：保存并退出 / 不保存退出 / 取消。</summary>
public partial class ExitConfirmDialog : ModalView
{
    public ExitConfirmDialog()
    {
        InitializeComponent();
        HookNarrowMode();
    }

    public ExitConfirmDialog(int unsavedCount) : this()
    {
        MessageText.Text = Localizer.F("Exit.UnsavedMessage", unsavedCount);
    }

    private void OnSaveAndExit(object? sender, RoutedEventArgs e) => Close(ExitChoice.SaveAndExit);

    private void OnExitWithoutSave(object? sender, RoutedEventArgs e) => Close(ExitChoice.ExitWithoutSave);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(ExitChoice.Cancel);
}
