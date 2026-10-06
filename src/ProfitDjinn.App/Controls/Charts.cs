using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ProfitDjinn.App.Infrastructure;

namespace ProfitDjinn.App.Controls;

/// <summary>A tooltip that follows the mouse over a chart.</summary>
internal sealed class ChartTip
{
    private readonly Popup _popup = new() { AllowsTransparency = true, Placement = PlacementMode.Relative, IsHitTestVisible = false };
    private readonly TextBlock _text = new() { FontSize = 12 };

    public ChartTip(UIElement owner)
    {
        _text.SetResourceReference(TextBlock.ForegroundProperty, "TooltipFg");
        var border = new Border { Padding = new Thickness(10, 5, 10, 5), CornerRadius = new CornerRadius(5), Child = _text };
        border.SetResourceReference(Border.BackgroundProperty, "TooltipBg");
        _popup.Child = border;
        _popup.PlacementTarget = owner;
    }

    public void Show(string text, Point at)
    {
        _text.Text = text;
        _popup.HorizontalOffset = at.X + 12;
        _popup.VerticalOffset = at.Y - 28;
        _popup.IsOpen = true;
    }

    public void Hide() => _popup.IsOpen = false;
}

/// <summary>
/// A vertical bar chart like 1.x's Chart.js one on the Revenue page: zero-based y axis with
/// nice "$" ticks, grid lines, rounded bars, a tooltip " $x.xx" on hover. Height follows
/// the width at 1.x's aspect ratio (3.5).
/// </summary>
public sealed class BarChart : FrameworkElement
{
    private IReadOnlyList<(string Label, double Value)> _data = Array.Empty<(string, double)>();
    private readonly ChartTip _tip;
    private int _hover = -1;
    private Rect[] _bars = Array.Empty<Rect>();

    public double AspectRatio { get; set; } = 3.5;

    public BarChart()
    {
        _tip = new ChartTip(this);
        MouseMove += OnMove;
        MouseLeave += (_, _) => { _hover = -1; _tip.Hide(); InvalidateVisual(); };
        Background = Brushes.Transparent;
    }

    private Brush Background { get; }

    public void SetData(IReadOnlyList<(string Label, double Value)> data)
    {
        _data = data;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) ? 600 : available.Width;
        return new Size(w, Math.Max(160, w / AspectRatio));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
        if (_data.Count == 0) return;
        var grid = Find("ChartGrid");
        var label = Find("ChartLabel");
        var bar = Find("ChartBar");
        var barHover = Find("ChartBarHover");
        var font = new Typeface((FontFamily)Application.Current.FindResource("BodyFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        double max = Math.Max(_data.Max(d => d.Value), 0);
        var (step, top) = NiceScale(max);
        int ticks = (int)Math.Round(top / step);

        double left = 0;
        var tickTexts = Enumerable.Range(0, ticks + 1)
            .Select(i => new FormattedText("$" + (i * step).ToString("#,##0.##", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 12, label, dpi))
            .ToList();
        left = tickTexts.Max(t => t.Width) + 10;
        double bottomPad = 22, topPad = 8;
        double plotH = h - bottomPad - topPad, plotW = w - left;

        var gridPen = new Pen(grid, 1);
        for (int i = 0; i <= ticks; i++)
        {
            double y = topPad + plotH - plotH * i / ticks;
            dc.DrawLine(gridPen, new Point(left, Math.Round(y) + 0.5), new Point(w, Math.Round(y) + 0.5));
            var t = tickTexts[i];
            dc.DrawText(t, new Point(left - 8 - t.Width, y - t.Height / 2));
        }

        double slot = plotW / _data.Count;
        double barW = Math.Min(slot * 0.8 * 0.9, 120);
        _bars = new Rect[_data.Count];
        for (int i = 0; i < _data.Count; i++)
        {
            double v = _data[i].Value;
            double bh = top > 0 ? plotH * v / top : 0;
            double x = left + slot * i + (slot - barW) / 2;
            var rect = new Rect(x, topPad + plotH - bh, barW, Math.Max(0, bh));
            _bars[i] = new Rect(left + slot * i, topPad, slot, plotH);
            if (bh > 0)
            {
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    double r = Math.Min(4, Math.Min(barW / 2, bh));
                    g.BeginFigure(new Point(rect.Left, rect.Bottom), true, true);
                    g.LineTo(new Point(rect.Left, rect.Top + r), true, false);
                    g.ArcTo(new Point(rect.Left + r, rect.Top), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    g.LineTo(new Point(rect.Right - r, rect.Top), true, false);
                    g.ArcTo(new Point(rect.Right, rect.Top + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    g.LineTo(new Point(rect.Right, rect.Bottom), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(i == _hover ? barHover : bar, null, geo);
            }
            var lt = new FormattedText(_data[i].Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 12, label, dpi);
            dc.DrawText(lt, new Point(left + slot * i + (slot - lt.Width) / 2, h - bottomPad + 4));
        }
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        int hit = Array.FindIndex(_bars, r => r.Contains(p));
        if (hit != _hover) { _hover = hit; InvalidateVisual(); }
        if (hit >= 0) _tip.Show($"{_data[hit].Label}:  {Ui.MoneyGrouped(_data[hit].Value)}", p);
        else _tip.Hide();
    }

    /// <summary>Chart.js-style nice ticks: step of 1, 2, 2.5 or 5 times a power of ten, about 5 ticks.</summary>
    internal static (double Step, double Top) NiceScale(double max)
    {
        if (max <= 0) return (1, 1);
        double raw = max / 5;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double norm = raw / mag;
        double nice = norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 2.5 ? 2.5 : norm <= 5 ? 5 : 10;
        double step = nice * mag;
        return (step, Math.Ceiling(max / step) * step);
    }

    private Brush Find(string key) => (Brush)(TryFindResource(key) ?? Brushes.Gray);
}

/// <summary>
/// A doughnut like 1.x's "Revenue by Customer" chart: slices from a fixed palette, 62%
/// cut-out, legend underneath, tooltip "$x.xx (p%)" on hover.
/// </summary>
public sealed class DoughnutChart : StackPanel
{
    /// <summary>1.x's palette, in order (revenue.html).</summary>
    public static readonly string[] Palette =
    {
        "#1c3458", "#2563eb", "#0891b2", "#059669", "#d97706", "#dc2626", "#7c3aed", "#db2777",
        "#65a30d", "#0f766e", "#9333ea", "#f59e0b", "#10b981", "#3b82f6", "#ef4444",
    };

    private readonly Ring _ring = new();
    private readonly WrapPanel _legend = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };

    /// <summary>2.7. A slice was clicked (its index). Set it and the slices show a hand cursor.</summary>
    public event Action<int>? Clicked
    {
        add => _ring.Clicked += value;
        remove => _ring.Clicked -= value;
    }

    public DoughnutChart()
    {
        Children.Add(_ring);
        Children.Add(_legend);
    }

    public void SetData(IReadOnlyList<(string Label, double Value)> data)
    {
        _ring.SetData(data);
        _legend.Children.Clear();
        var conv = new BrushConverter();
        for (int i = 0; i < data.Count; i++)
        {
            var swatch = new Border { Width = 28, Height = 10, Background = (Brush)conv.ConvertFromString(Palette[i % Palette.Length])!, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            var text = new TextBlock { Text = data[i].Label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }.WithResource(TextBlock.ForegroundProperty, "ChartLabel");
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 2, 6, 2) };
            item.Children.Add(swatch);
            item.Children.Add(text);
            _legend.Children.Add(item);
        }
    }

    private sealed class Ring : FrameworkElement
    {
        private IReadOnlyList<(string Label, double Value)> _data = Array.Empty<(string, double)>();
        private readonly ChartTip _tip;
        private int _hover = -1;

        public event Action<int>? Clicked;

        public Ring()
        {
            _tip = new ChartTip(this);
            MouseMove += OnMove;
            MouseLeave += (_, _) => { _hover = -1; _tip.Hide(); InvalidateVisual(); };
            MouseLeftButtonUp += (_, _) => { if (_hover >= 0) Clicked?.Invoke(_hover); };
        }

        public void SetData(IReadOnlyList<(string Label, double Value)> data)
        {
            _data = data;
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size available)
        {
            double w = double.IsInfinity(available.Width) ? 300 : available.Width;
            return new Size(w, Math.Min(w, 260));
        }

        private double Total => _data.Sum(d => Math.Max(0, d.Value));

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            double total = Total;
            if (total <= 0) return;
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            double outer = Math.Min(ActualWidth, ActualHeight) / 2 - 6, inner = outer * 0.62;
            var conv = new BrushConverter();
            var surface = (Brush)(TryFindResource("Surface") ?? Brushes.White);
            double angle = -90;
            for (int i = 0; i < _data.Count; i++)
            {
                double sweep = 360 * Math.Max(0, _data[i].Value) / total;
                if (sweep <= 0) continue;
                double grow = i == _hover ? 4 : 0;
                var fill = (Brush)conv.ConvertFromString(Palette[i % Palette.Length])!;
                dc.DrawGeometry(fill, new Pen(surface, 2), Slice(center, outer + grow, inner, angle, Math.Min(sweep, 359.99)));
                angle += sweep;
            }
        }

        private static Geometry Slice(Point c, double ro, double ri, double startDeg, double sweepDeg)
        {
            Point P(double r, double deg) => new(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));
            bool large = sweepDeg > 180;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(P(ro, startDeg), true, true);
                ctx.ArcTo(P(ro, startDeg + sweepDeg), new Size(ro, ro), 0, large, SweepDirection.Clockwise, true, false);
                ctx.LineTo(P(ri, startDeg + sweepDeg), true, false);
                ctx.ArcTo(P(ri, startDeg), new Size(ri, ri), 0, large, SweepDirection.Counterclockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            var c = new Point(ActualWidth / 2, ActualHeight / 2);
            double outer = Math.Min(ActualWidth, ActualHeight) / 2 - 6, inner = outer * 0.62;
            double dist = (p - c).Length;
            int hit = -1;
            double total = Total;
            if (dist >= inner && dist <= outer + 4 && total > 0)
            {
                double deg = (Math.Atan2(p.Y - c.Y, p.X - c.X) * 180 / Math.PI + 90 + 360) % 360;
                double acc = 0;
                for (int i = 0; i < _data.Count; i++)
                {
                    acc += 360 * Math.Max(0, _data[i].Value) / total;
                    if (deg < acc) { hit = i; break; }
                }
            }
            if (hit != _hover) { _hover = hit; InvalidateVisual(); }
            Cursor = hit >= 0 && Clicked is not null ? Cursors.Hand : null;
            if (hit >= 0)
            {
                double pct = _data[hit].Value / total * 100;
                _tip.Show($"{_data[hit].Label}:  {Ui.MoneyGrouped(_data[hit].Value)} ({pct.ToString("0.0", CultureInfo.InvariantCulture)}%)", p);
            }
            else _tip.Hide();
        }
    }
}

/// <summary>
/// 2.2: two bars per slot, income (Success) beside expenses (Danger), for the Profit &amp; Loss
/// page. Same axis, ticks, grid and hover tooltip as <see cref="BarChart"/>; the tooltip also
/// gives the net. A legend sits under the plot.
/// </summary>
public sealed class PairBarChart : FrameworkElement
{
    private IReadOnlyList<(string Label, double A, double B)> _data = Array.Empty<(string, double, double)>();
    private readonly ChartTip _tip;
    private int _hover = -1;
    private Rect[] _slots = Array.Empty<Rect>();

    public double AspectRatio { get; set; } = 3.2;
    public string LabelA { get; set; } = "Income";
    public string LabelB { get; set; } = "Expenses";

    /// <summary>2.7. A bar pair was clicked (its index). Set it and the bars show a hand cursor.</summary>
    public event Action<int>? Clicked;

    public PairBarChart()
    {
        _tip = new ChartTip(this);
        MouseMove += OnMove;
        MouseLeave += (_, _) => { _hover = -1; _tip.Hide(); InvalidateVisual(); };
        MouseLeftButtonUp += (_, _) => { if (_hover >= 0) Clicked?.Invoke(_hover); };
    }

    public void SetData(IReadOnlyList<(string Label, double A, double B)> data)
    {
        _data = data;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) ? 600 : available.Width;
        return new Size(w, Math.Max(180, w / AspectRatio));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (_data.Count == 0) return;
        var label = Find("ChartLabel");
        var brushA = Find("Success");
        var brushB = Find("Danger");
        var font = new Typeface((FontFamily)Application.Current.FindResource("BodyFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Text(string s) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 12, label, dpi);

        double max = Math.Max(_data.Max(d => Math.Max(d.A, d.B)), 0);
        var (step, top) = BarChart.NiceScale(max);
        int ticks = (int)Math.Round(top / step);
        var tickTexts = Enumerable.Range(0, ticks + 1).Select(i => Text("$" + (i * step).ToString("#,##0.##", CultureInfo.InvariantCulture))).ToList();
        double left = tickTexts.Max(t => t.Width) + 10;
        double legendH = 22, bottomPad = 22 + legendH, topPad = 8;
        double plotH = h - bottomPad - topPad, plotW = w - left;

        var gridPen = new Pen(Find("ChartGrid"), 1);
        for (int i = 0; i <= ticks; i++)
        {
            double y = topPad + plotH - plotH * i / ticks;
            dc.DrawLine(gridPen, new Point(left, Math.Round(y) + 0.5), new Point(w, Math.Round(y) + 0.5));
            dc.DrawText(tickTexts[i], new Point(left - 8 - tickTexts[i].Width, y - tickTexts[i].Height / 2));
        }

        double slot = plotW / _data.Count;
        double barW = Math.Min(slot * 0.8 / 2 * 0.9, 48);
        _slots = new Rect[_data.Count];
        for (int i = 0; i < _data.Count; i++)
        {
            double x0 = left + slot * i + (slot - barW * 2 - 2) / 2;
            Bar(dc, x0, _data[i].A, brushA, i == _hover);
            Bar(dc, x0 + barW + 2, _data[i].B, brushB, i == _hover);
            _slots[i] = new Rect(left + slot * i, topPad, slot, plotH);
            var lt = Text(_data[i].Label);
            dc.DrawText(lt, new Point(left + slot * i + (slot - lt.Width) / 2, topPad + plotH + 4));
        }

        // legend
        double lx = left, ly = h - legendH + 4;
        foreach (var (name, brush) in new[] { (LabelA, brushA), (LabelB, brushB) })
        {
            dc.DrawRoundedRectangle(brush, null, new Rect(lx, ly + 3, 22, 10), 2, 2);
            var t = Text(name);
            dc.DrawText(t, new Point(lx + 28, ly));
            lx += 28 + t.Width + 20;
        }

        void Bar(DrawingContext d, double x, double v, Brush brush, bool hover)
        {
            double bh = top > 0 ? plotH * Math.Max(0, v) / top : 0;
            if (bh <= 0) return;
            var rect = new Rect(x, topPad + plotH - bh, barW, bh);
            double r = Math.Min(3, Math.Min(barW / 2, bh));
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(rect.Left, rect.Bottom), true, true);
                g.LineTo(new Point(rect.Left, rect.Top + r), true, false);
                g.ArcTo(new Point(rect.Left + r, rect.Top), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                g.LineTo(new Point(rect.Right - r, rect.Top), true, false);
                g.ArcTo(new Point(rect.Right, rect.Top + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                g.LineTo(new Point(rect.Right, rect.Bottom), true, false);
            }
            geo.Freeze();
            d.PushOpacity(hover ? 1.0 : 0.85);
            d.DrawGeometry(brush, null, geo);
            d.Pop();
        }
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        int hit = Array.FindIndex(_slots, r => r.Contains(p));
        if (hit != _hover) { _hover = hit; InvalidateVisual(); }
        Cursor = hit >= 0 && Clicked is not null ? Cursors.Hand : null;
        if (hit >= 0)
        {
            var d = _data[hit];
            _tip.Show($"{d.Label}   {LabelA} {Ui.MoneyGrouped(d.A)}   {LabelB} {Ui.MoneyGrouped(d.B)}   Net {Ui.MoneyGrouped(d.A - d.B)}", p);
        }
        else _tip.Hide();
    }

    private Brush Find(string key) => (Brush)(TryFindResource(key) ?? Brushes.Gray);
}
