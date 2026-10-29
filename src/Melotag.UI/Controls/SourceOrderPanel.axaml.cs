namespace Melotag.UI.Controls;

using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Melotag.Core.Sources;

/// <summary>单个类别（标签 / 封面 / 歌词）的在线源启用与排序。列表顺序即查询优先级。</summary>
public partial class SourceOrderPanel : UserControl
{
    public static readonly StyledProperty<SourceCapability> CapabilityProperty =
        AvaloniaProperty.Register<SourceOrderPanel, SourceCapability>(nameof(Capability));

    private readonly ObservableCollection<SourceOrderItem> _items = [];
    private IReadOnlyList<string> _defaults = [];

    public SourceOrderPanel()
    {
        InitializeComponent();
        SourcesList.ItemsSource = _items;
    }

    /// <summary>本面板负责的能力；只列出支持该能力的源。</summary>
    public SourceCapability Capability
    {
        get => GetValue(CapabilityProperty);
        set => SetValue(CapabilityProperty, value);
    }

    /// <summary>按配置顺序载入；不在配置里的源排在末尾并处于未启用状态。</summary>
    public void Load(IEnumerable<ITagSource> sources, IReadOnlyList<string> order, IReadOnlyList<string> defaults)
    {
        _defaults = defaults;
        _items.Clear();

        var rows = sources
            .Where(s => (s.Capabilities & Capability) != 0)
            .Select(s => (Source: s, Rank: RankOf(order, s.Id)))
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Source.DisplayName, StringComparer.OrdinalIgnoreCase);

        foreach (var (source, _) in rows)
        {
            var enabled = order.Contains(source.Id, StringComparer.OrdinalIgnoreCase);
            _items.Add(new SourceOrderItem(source.Id, source.DisplayName, Describe(source.Capabilities), enabled));
        }
    }

    /// <summary>当前勾选的源 id，按列表顺序。</summary>
    public List<string> GetOrderedIds() =>
        _items.Where(i => i.Enabled).Select(i => i.Id).ToList();

    private static int RankOf(IReadOnlyList<string> order, string id)
    {
        for (var i = 0; i < order.Count; i++)
            if (string.Equals(order[i], id, StringComparison.OrdinalIgnoreCase))
                return i;
        return int.MaxValue;
    }

    private static string Describe(SourceCapability capability)
    {
        var parts = new List<string>();
        if ((capability & SourceCapability.Tags) != 0) parts.Add("标签");
        if ((capability & SourceCapability.Cover) != 0) parts.Add("封面");
        if ((capability & SourceCapability.Lyrics) != 0) parts.Add("歌词");
        return string.Join(" · ", parts);
    }

    private void OnMoveUp(object? sender, RoutedEventArgs e) => Move(sender, -1);

    private void OnMoveDown(object? sender, RoutedEventArgs e) => Move(sender, 1);

    private void Move(object? sender, int delta)
    {
        if (sender is not Control { DataContext: SourceOrderItem item }) return;

        var index = _items.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _items.Count) return;

        _items.Move(index, target);
    }

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        var ordered = _items.OrderBy(i => RankOf(_defaults, i.Id)).ToList();
        _items.Clear();
        foreach (var item in ordered)
            item.Enabled = _defaults.Contains(item.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var item in ordered)
            _items.Add(item);
    }

    public sealed class SourceOrderItem : INotifyPropertyChanged
    {
        private bool _enabled;

        public SourceOrderItem(string id, string name, string capabilityText, bool enabled)
        {
            Id = id;
            Name = name;
            CapabilityText = capabilityText;
            _enabled = enabled;
        }

        public string Id { get; }
        public string Name { get; }
        public string CapabilityText { get; }

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
