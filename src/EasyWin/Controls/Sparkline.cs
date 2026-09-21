using System.Windows;
using System.Windows.Media;

namespace EasyWin.Controls;

/// <summary>极简趋势图:把一组数值画成折线 + 半透明面积填充,仪表盘 CPU/内存/网速趋势用。</summary>
public class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>样本序列(最新值在末尾),整体替换时触发重绘。</summary>
    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    static Sparkline() =>
        ClipToBoundsProperty.OverrideMetadata(typeof(Sparkline), new FrameworkPropertyMetadata(true));

    protected override void OnRender(DrawingContext dc)
    {
        var values = Values;
        var w = ActualWidth;
        var h = ActualHeight;
        if (values is not { Count: >= 2 } || w < 4 || h < 4) return;

        double max = 0;
        foreach (var v in values) max = Math.Max(max, v);
        if (max <= 0) max = 1;

        var stepX = w / (values.Count - 1);
        Point PointOf(int i) => new(i * stepX, h - 2 - (values[i] / max) * (h - 4));

        var accent = (TryFindResource("AccentFillColorDefaultBrush") as Brush) ?? Brushes.DodgerBlue;

        // 面积填充:折线 + 底边闭合
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(PointOf(0), isFilled: true, isClosed: true);
            for (var i = 1; i < values.Count; i++) ctx.LineTo(PointOf(i), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(w, h), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(0, h), isStroked: false, isSmoothJoin: false);
        }
        area.Freeze();

        var fill = accent.Clone();
        fill.Opacity = 0.18;
        fill.Freeze();
        dc.DrawGeometry(fill, null, area);

        // 折线描边
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(PointOf(0), isFilled: false, isClosed: false);
            for (var i = 1; i < values.Count; i++) ctx.LineTo(PointOf(i), isStroked: true, isSmoothJoin: false);
        }
        line.Freeze();
        var pen = new Pen(accent.Clone(), 1.6);
        pen.Freeze();
        dc.DrawGeometry(null, pen, line);
    }
}
