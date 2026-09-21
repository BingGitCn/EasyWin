using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace EasyWin.Controls;

/// <summary>
/// 空状态占位:图标 + 标题 + 说明 + 可选操作按钮(放在 Actions 里)。
/// 页面上用 Style 触发器按集合 Count 控制显隐。
/// </summary>
public partial class EmptyState : UserControl
{
    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(
        nameof(Symbol), typeof(SymbolRegular), typeof(EmptyState), new PropertyMetadata(SymbolRegular.Info24));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(EmptyState), new PropertyMetadata(null));

    public SymbolRegular Symbol
    {
        get => (SymbolRegular)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public EmptyState()
    {
        InitializeComponent();
    }
}
