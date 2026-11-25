using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace PodpivasEngine.Views;

public sealed class SquiggleRenderer : IBackgroundRenderer
{
    private const double Step = 3;
    private const double Height = 3;
    private static readonly Pen Stroke = new(new SolidColorBrush(Color.Parse("#0EA800")), 1.2);
    private readonly List<ISegment> _segments = [];

    public KnownLayer Layer => KnownLayer.Selection;

    public void SetErrorLine(TextDocument document, int line)
    {
        _segments.Clear();
        if (line <= 0 || document.LineCount == 0)
            return;
        var number = Math.Min(line, document.LineCount);
        while (number > 0)
        {
            var current = document.GetLineByNumber(number);
            var start = current.Offset;
            while (start < current.EndOffset && char.IsWhiteSpace(document.GetCharAt(start)))
                start++;
            if (start < current.EndOffset)
            {
                _segments.Add(new TextSegment { StartOffset = start, Length = current.EndOffset - start });
                return;
            }
            number--;
        }
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_segments.Count == 0)
            return;
        foreach (var segment in _segments)
        {
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                var baseline = rect.Bottom - Height;
                drawingContext.DrawGeometry(null, Stroke, CreateWave(new Point(rect.Left, baseline), rect.Right));
            }
        }
    }

    private static Geometry CreateWave(Point start, double right)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(start, false);
        var up = true;
        for (var x = start.X + Step; x < right; x += Step)
        {
            context.LineTo(new Point(x, up ? start.Y - Height : start.Y));
            up = !up;
        }
        context.LineTo(new Point(right, start.Y));
        return geometry;
    }
}
