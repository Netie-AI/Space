using System.Windows;
using System.Windows.Controls;

namespace NetieSpace.Apps.Shell.ImagePanels;

/// <summary>
/// One option control of the tool page, reached without the page's help: the page builds a block (label, control) per
/// descriptor in order, so block i is option i. The visual panel reads and writes the same controls a run reads, so the
/// controls stay the single source of truth.
/// </summary>
internal sealed class OptionBinding
{
    private readonly Func<string> _get;
    private readonly Action<string> _set;

    private OptionBinding(string key, FrameworkElement block, Func<string> get, Action<string> set)
    {
        Key = key;
        Block = block;
        _get = get;
        _set = set;
    }

    public string Key { get; }
    /// <summary>The label and control together; the panel hides it or moves it into a fold-out.</summary>
    public FrameworkElement Block { get; }
    public string Get() => _get();
    public void Set(string value) => _set(value);

    /// <summary>Binds every option block under <paramref name="host"/>; <paramref name="changed"/> gets the key on every edit.</summary>
    public static Dictionary<string, OptionBinding> Bind(ToolPageModel model, Panel host, Action<string> changed)
    {
        var map = new Dictionary<string, OptionBinding>(StringComparer.Ordinal);
        var n = Math.Min(model.Options.Count, host.Children.Count);
        for (var i = 0; i < n; i++)
        {
            if (host.Children[i] is not StackPanel block)
                continue;
            var key = model.Options[i].Key;
            var b = Make(key, block, changed);
            if (b is not null)
                map[key] = b;
        }
        return map;
    }

    private static OptionBinding? Make(string key, StackPanel block, Action<string> changed)
    {
        if (block.Children.Count > 0 && block.Children[0] is CheckBox cb)
        {
            cb.Checked += (_, _) => changed(key);
            cb.Unchecked += (_, _) => changed(key);
            return new OptionBinding(key, block, () => cb.IsChecked == true ? "true" : "false", v => cb.IsChecked = v == "true");
        }
        if (block.Children.Count < 2)
            return null;
        switch (Leaf(block.Children[1]))
        {
            case ComboBox combo:
                combo.SelectionChanged += (_, _) => changed(key);
                return new OptionBinding(key, block, () => combo.SelectedValue as string ?? "", v =>
                {
                    foreach (var item in combo.Items)
                        if (item is ToolPageView.ChoiceItem c && string.Equals(c.Value, v, StringComparison.OrdinalIgnoreCase))
                        {
                            combo.SelectedItem = item;
                            return;
                        }
                });
            case TextBox tb:
                tb.TextChanged += (_, _) => changed(key);
                return new OptionBinding(key, block, () => tb.Text, v => tb.Text = v);
            default:
                return null;
        }
    }

    private static FrameworkElement? Leaf(UIElement control)
    {
        switch (control)
        {
            case ComboBox or TextBox:
                return (FrameworkElement)control;
            case Panel p:
                foreach (UIElement child in p.Children)
                    if (child is TextBox or ComboBox)
                        return (FrameworkElement)child;
                return null;
            default:
                return null;
        }
    }
}
