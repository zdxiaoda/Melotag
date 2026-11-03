using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using Melotag.UI.Platform;
using AndroidUri = Android.Net.Uri;

namespace Melotag.Droid;

[Activity(
    Label = "音签 Melotag",
    Theme = "@style/MelotagTheme.NoActionBar",
    Icon = "@drawable/Icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize |
                          ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode |
                          ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden)]
public class MainActivity : AvaloniaMainActivity
{
    private const int PickFilesRequestCode = 0x4D54;
    private const int PickFolderRequestCode = 0x4D55;

    private static TaskCompletionSource<IReadOnlyList<AndroidUri>>? _pickedFiles;
    private static TaskCompletionSource<AndroidUri?>? _pickedFolder;

    internal static MainActivity? Current { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Current = this;

        // App 在 base.OnCreate 里初始化，文件选择器必须先就位。
        PlatformServices.FilePicker = SafDocumentPicker.Instance;

        base.OnCreate(savedInstanceState);
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(Current, this)) Current = null;
        base.OnDestroy();
    }

    /// <summary>弹出系统 SAF 选择器，返回用户选中的文档 URI（取消则返回空）。</summary>
    internal Task<IReadOnlyList<AndroidUri>> PickDocumentsAsync()
    {
        if (_pickedFiles is { } pending) return pending.Task;

        var completion = new TaskCompletionSource<IReadOnlyList<AndroidUri>>();
        _pickedFiles = completion;

        try
        {
            var intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("*/*");
            intent.PutExtra(Intent.ExtraAllowMultiple, true);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            StartActivityForResult(intent, PickFilesRequestCode);
        }
        catch (Exception ex)
        {
            _pickedFiles = null;
            completion.SetException(ex);
        }

        return completion.Task;
    }

    /// <summary>弹出 SAF 目录选择器，返回用户选中的目录树 URI（取消则返回 null）。</summary>
    internal Task<AndroidUri?> PickFolderAsync()
    {
        if (_pickedFolder is { } pending) return pending.Task;

        var completion = new TaskCompletionSource<AndroidUri?>();
        _pickedFolder = completion;

        try
        {
            var intent = new Intent(Intent.ActionOpenDocumentTree);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission |
                            ActivityFlags.GrantWriteUriPermission |
                            ActivityFlags.GrantPersistableUriPermission);
            StartActivityForResult(intent, PickFolderRequestCode);
        }
        catch (Exception ex)
        {
            _pickedFolder = null;
            completion.SetException(ex);
        }

        return completion.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode == PickFolderRequestCode)
        {
            var pendingFolder = _pickedFolder;
            _pickedFolder = null;
            if (pendingFolder is null) return;

            var tree = resultCode == Result.Ok ? data?.Data : null;
            if (tree is not null) TryPersist(tree);
            pendingFolder.SetResult(tree);
            return;
        }

        if (requestCode != PickFilesRequestCode) return;

        var completion = _pickedFiles;
        _pickedFiles = null;
        if (completion is null) return;

        var uris = new List<AndroidUri>();
        if (resultCode == Result.Ok && data is not null)
        {
            if (data.Data is { } single) uris.Add(single);

            if (data.ClipData is { } clip)
            {
                for (var i = 0; i < clip.ItemCount; i++)
                    if (clip.GetItemAt(i)?.Uri is { } uri) uris.Add(uri);
            }
        }

        completion.SetResult(uris);
    }

    private void TryPersist(AndroidUri uri)
    {
        try
        {
            ContentResolver!.TakePersistableUriPermission(
                uri,
                ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        }
        catch
        {
            // 少数 provider 不支持持久化授权，忽略即可
        }
    }
}
