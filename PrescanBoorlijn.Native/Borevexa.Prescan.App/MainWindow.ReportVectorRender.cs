using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Borevexa.Prescan.App;

public partial class MainWindow
{
    private static string BuildVectorReportPageHtml(FrameworkElement page, int pageNumber)
    {
        PrepareReportPageForVectorExport(page);
        var width = Math.Max(1, page.ActualWidth);
        var height = Math.Max(1, page.ActualHeight);
        var landscape = width > height;
        var scale = landscape ? 297d / width : 210d / width;
        var html = new StringBuilder();
        html.AppendLine($"<main class=\"sheet{(landscape ? " landscape" : "")}\" data-page=\"{pageNumber}\">");
        AppendVectorElementHtml(html, page, page, scale);
        html.AppendLine("</main>");
        return html.ToString();
    }

    private static void PrepareReportPageForVectorExport(FrameworkElement page)
    {
        page.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = page.Width > 0 ? page.Width : Math.Max(1, page.DesiredSize.Width);
        var height = page.Height > 0 ? page.Height : Math.Max(page.MinHeight, page.DesiredSize.Height);
        if (double.IsNaN(width) || width < 2) width = 840;
        if (double.IsNaN(height) || height < 2) height = 1188;
        page.Arrange(new Rect(0, 0, width, height));
        page.UpdateLayout();
    }

    private static void AppendVectorElementHtml(StringBuilder html, DependencyObject current, FrameworkElement root, double scale)
    {
        if (current is FrameworkElement)
        {
            if (current is TextBlock textBlock)
            {
                AppendVectorTextHtml(html, textBlock, root, scale);
                return;
            }
            if (current is Image image)
            {
                AppendVectorImageHtml(html, image, root, scale);
                return;
            }
            if (current is Shape shape)
            {
                AppendVectorShapeHtml(html, shape, root, scale);
                return;
            }
            if (current is Border border)
            {
                AppendVectorBorderHtml(html, border, root, scale);
            }
        }

        var count = VisualTreeHelper.GetChildrenCount(current);
        for (var i = 0; i < count; i++)
        {
            AppendVectorElementHtml(html, VisualTreeHelper.GetChild(current, i), root, scale);
        }
    }

    private static void AppendVectorBorderHtml(StringBuilder html, Border border, FrameworkElement root, double scale)
    {
        if (!TryGetVectorBounds(border, root, scale, out var bounds)) return;
        var background = BrushToCss(border.Background);
        var borderBrush = BrushToCss(border.BorderBrush);
        var borderWidth = Math.Max(Math.Max(border.BorderThickness.Left, border.BorderThickness.Right), Math.Max(border.BorderThickness.Top, border.BorderThickness.Bottom)) * scale;
        if (background == "transparent" && (borderBrush == "transparent" || borderWidth <= 0.01)) return;
        var radius = Math.Max(0, border.CornerRadius.TopLeft * scale);
        if (!TryGetVectorClip(border, root, scale, bounds, out var clip)) return;
        var style = $"background:{background};border:{CssMm(borderWidth)} solid {borderBrush};border-radius:{CssMm(radius)}";
        AppendClippedDiv(html, bounds, clip, style);
    }

    private static void AppendVectorTextHtml(StringBuilder html, TextBlock textBlock, FrameworkElement root, double scale)
    {
        if (!TryGetVectorBounds(textBlock, root, scale, out var bounds)) return;
        if (!TryGetVectorClip(textBlock, root, scale, bounds, out var clip)) return;
        var color = BrushToCss(textBlock.Foreground);
        var weight = textBlock.FontWeight.ToOpenTypeWeight();
        var style = textBlock.FontStyle == FontStyles.Italic ? "italic" : "normal";
        var align = textBlock.TextAlignment switch
        {
            TextAlignment.Center => "center",
            TextAlignment.Right => "right",
            TextAlignment.Justify => "justify",
            _ => "left"
        };
        var transform = textBlock.RenderTransform is RotateTransform rotate && Math.Abs(rotate.Angle) > 0.01
            ? $";transform:rotate({rotate.Angle.ToString("0.###", CultureInfo.InvariantCulture)}deg);transform-origin:left top"
            : "";
        var lineHeight = double.IsNaN(textBlock.LineHeight) || textBlock.LineHeight <= 0
            ? textBlock.FontSize * 1.25
            : textBlock.LineHeight;
        var encoded = System.Net.WebUtility.HtmlEncode(textBlock.Text ?? "");
        var width = Math.Max(bounds.Width, textBlock.FontSize * scale);
        var height = Math.Max(bounds.Height, lineHeight * scale);
        var textStyle = $"color:{color};font-size:{CssMm(textBlock.FontSize * scale)};font-weight:{weight};font-style:{style};text-align:{align};line-height:{CssMm(lineHeight * scale)}{transform}";
        AppendClippedContent(
            html,
            bounds,
            clip,
            innerClass: "vtext",
            innerStyle: $"width:{CssMm(width)};height:{CssMm(height)};{textStyle}",
            innerHtml: encoded);
    }

    private static void AppendVectorImageHtml(StringBuilder html, Image image, FrameworkElement root, double scale)
    {
        if (!TryGetVectorBounds(image, root, scale, out var bounds)) return;
        if (!TryGetVectorClip(image, root, scale, bounds, out var clip)) return;
        var source = ImageSourceToDataUri(image.Source);
        if (string.IsNullOrWhiteSpace(source)) return;
        var encodedSource = System.Net.WebUtility.HtmlEncode(source);
        if (IsSameRect(bounds, clip))
        {
            html.AppendLine($"<img class=\"vitem vimage\" src=\"{encodedSource}\" style=\"left:{CssMm(bounds.X)};top:{CssMm(bounds.Y)};width:{CssMm(bounds.Width)};height:{CssMm(bounds.Height)}\" alt=\"rapportbeeld\" />");
            return;
        }

        html.AppendLine($"<div class=\"vitem\" style=\"left:{CssMm(clip.X)};top:{CssMm(clip.Y)};width:{CssMm(clip.Width)};height:{CssMm(clip.Height)}\">");
        html.AppendLine($"<img class=\"vinner vimage\" src=\"{encodedSource}\" style=\"left:{CssMm(bounds.X - clip.X)};top:{CssMm(bounds.Y - clip.Y)};width:{CssMm(bounds.Width)};height:{CssMm(bounds.Height)}\" alt=\"rapportbeeld\" />");
        html.AppendLine("</div>");
    }

    private static void AppendVectorShapeHtml(StringBuilder html, Shape shape, FrameworkElement root, double scale)
    {
        switch (shape)
        {
            case Line line:
                AppendVectorLineHtml(html, line, root, scale);
                return;
            case Polyline polyline:
                AppendVectorPolylineHtml(html, polyline, root, scale);
                return;
            case Polygon polygon:
                AppendVectorPolygonHtml(html, polygon, root, scale);
                return;
            case Ellipse:
            case Rectangle:
                AppendVectorBasicShapeHtml(html, shape, root, scale);
                return;
            case System.Windows.Shapes.Path path:
                AppendVectorPathHtml(html, path, root, scale);
                return;
        }
    }

    private static void AppendVectorBasicShapeHtml(StringBuilder html, Shape shape, FrameworkElement root, double scale)
    {
        if (!TryGetVectorBounds(shape, root, scale, out var bounds)) return;
        if (!TryGetVectorClip(shape, root, scale, bounds, out var clip)) return;
        var radius = shape is Ellipse ? "50%" : "0";
        var style = $"background:{BrushToCss(shape.Fill)};border:{CssMm(shape.StrokeThickness * scale)} solid {BrushToCss(shape.Stroke)};border-radius:{radius}";
        AppendClippedDiv(html, bounds, clip, style);
    }

    private static void AppendVectorLineHtml(StringBuilder html, Line line, FrameworkElement root, double scale)
    {
        var p1 = TransformVectorPoint(line, root, new Point(line.X1, line.Y1));
        var p2 = TransformVectorPoint(line, root, new Point(line.X2, line.Y2));
        var minX = Math.Min(p1.X, p2.X) * scale;
        var minY = Math.Min(p1.Y, p2.Y) * scale;
        var width = Math.Max(0.2, Math.Abs(p1.X - p2.X) * scale);
        var height = Math.Max(0.2, Math.Abs(p1.Y - p2.Y) * scale);
        var x1 = (p1.X * scale - minX).ToString("0.###", CultureInfo.InvariantCulture);
        var y1 = (p1.Y * scale - minY).ToString("0.###", CultureInfo.InvariantCulture);
        var x2 = (p2.X * scale - minX).ToString("0.###", CultureInfo.InvariantCulture);
        var y2 = (p2.Y * scale - minY).ToString("0.###", CultureInfo.InvariantCulture);
        var bounds = new Rect(minX, minY, width, height);
        if (!TryGetVectorClip(line, root, scale, bounds, out var clip)) return;
        var svg = $"<line x1=\"{x1}\" y1=\"{y1}\" x2=\"{x2}\" y2=\"{y2}\" stroke=\"{BrushToCss(line.Stroke)}\" stroke-width=\"{(line.StrokeThickness * scale).ToString("0.###", CultureInfo.InvariantCulture)}\" stroke-linecap=\"round\" />";
        AppendClippedSvg(html, bounds, clip, width, height, svg);
    }

    private static void AppendVectorPolylineHtml(StringBuilder html, Polyline polyline, FrameworkElement root, double scale)
    {
        AppendVectorPointShapeHtml(html, polyline, root, scale, polyline.Points, "polyline");
    }

    private static void AppendVectorPolygonHtml(StringBuilder html, Polygon polygon, FrameworkElement root, double scale)
    {
        AppendVectorPointShapeHtml(html, polygon, root, scale, polygon.Points, "polygon");
    }

    private static void AppendVectorPointShapeHtml(StringBuilder html, Shape shape, FrameworkElement root, double scale, PointCollection points, string tag)
    {
        if (points.Count == 0) return;
        var transformed = points.Select(point => TransformVectorPoint(shape, root, point)).ToList();
        var minX = transformed.Min(point => point.X) * scale;
        var minY = transformed.Min(point => point.Y) * scale;
        var width = Math.Max(0.2, (transformed.Max(point => point.X) - transformed.Min(point => point.X)) * scale);
        var height = Math.Max(0.2, (transformed.Max(point => point.Y) - transformed.Min(point => point.Y)) * scale);
        var svgPoints = string.Join(" ", transformed.Select(point => $"{(point.X * scale - minX).ToString("0.###", CultureInfo.InvariantCulture)},{(point.Y * scale - minY).ToString("0.###", CultureInfo.InvariantCulture)}"));
        var fill = tag == "polygon" ? BrushToCss(shape.Fill) : "none";
        var bounds = new Rect(minX, minY, width, height);
        if (!TryGetVectorClip(shape, root, scale, bounds, out var clip)) return;
        var svg = $"<{tag} points=\"{svgPoints}\" fill=\"{fill}\" stroke=\"{BrushToCss(shape.Stroke)}\" stroke-width=\"{(shape.StrokeThickness * scale).ToString("0.###", CultureInfo.InvariantCulture)}\" stroke-linejoin=\"round\" stroke-linecap=\"round\" />";
        AppendClippedSvg(html, bounds, clip, width, height, svg);
    }

    private static void AppendVectorPathHtml(StringBuilder html, System.Windows.Shapes.Path path, FrameworkElement root, double scale)
    {
        if (!TryGetVectorBounds(path, root, scale, out var bounds)) return;
        if (!TryGetVectorClip(path, root, scale, bounds, out var clip)) return;
        var data = path.Data?.ToString(CultureInfo.InvariantCulture) ?? "";
        if (string.IsNullOrWhiteSpace(data)) return;
        var viewWidth = Math.Max(1, path.ActualWidth) * scale;
        var viewHeight = Math.Max(1, path.ActualHeight) * scale;
        var svg = $"<path d=\"{System.Net.WebUtility.HtmlEncode(data)}\" fill=\"{BrushToCss(path.Fill)}\" stroke=\"{BrushToCss(path.Stroke)}\" stroke-width=\"{(path.StrokeThickness * scale).ToString("0.###", CultureInfo.InvariantCulture)}\" />";
        AppendClippedSvg(html, bounds, clip, viewWidth, viewHeight, svg);
    }

    private static bool TryGetVectorBounds(FrameworkElement element, FrameworkElement root, double scale, out Rect bounds)
    {
        bounds = Rect.Empty;
        try
        {
            var point = ReferenceEquals(element, root)
                ? new Point(0, 0)
                : element.TransformToAncestor(root).Transform(new Point(0, 0));
            var actualWidth = element.ActualWidth > 0 ? element.ActualWidth : element.DesiredSize.Width;
            var actualHeight = element.ActualHeight > 0 ? element.ActualHeight : element.DesiredSize.Height;
            if (!double.IsFinite(actualWidth) || !double.IsFinite(actualHeight) || actualWidth <= 0 || actualHeight <= 0) return false;
            bounds = new Rect(point.X * scale, point.Y * scale, actualWidth * scale, actualHeight * scale);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetVectorClip(FrameworkElement element, FrameworkElement root, double scale, Rect bounds, out Rect clip)
    {
        clip = new Rect(0, 0, Math.Max(1, root.ActualWidth) * scale, Math.Max(1, root.ActualHeight) * scale);
        var current = VisualTreeHelper.GetParent(element);
        while (current is not null)
        {
            if (current is FrameworkElement ancestor && TryGetVectorBounds(ancestor, root, scale, out var ancestorBounds))
            {
                clip.Intersect(ancestorBounds);
                if (clip.IsEmpty) return false;
            }

            if (ReferenceEquals(current, root)) break;
            current = VisualTreeHelper.GetParent(current);
        }

        clip.Intersect(bounds);
        return !clip.IsEmpty && clip.Width > 0.01 && clip.Height > 0.01;
    }

    private static void AppendClippedDiv(StringBuilder html, Rect bounds, Rect clip, string style)
    {
        if (IsSameRect(bounds, clip))
        {
            html.AppendLine($"<div class=\"vitem\" style=\"left:{CssMm(bounds.X)};top:{CssMm(bounds.Y)};width:{CssMm(bounds.Width)};height:{CssMm(bounds.Height)};{style}\"></div>");
            return;
        }

        html.AppendLine($"<div class=\"vitem\" style=\"left:{CssMm(clip.X)};top:{CssMm(clip.Y)};width:{CssMm(clip.Width)};height:{CssMm(clip.Height)}\">");
        html.AppendLine($"<div class=\"vinner\" style=\"left:{CssMm(bounds.X - clip.X)};top:{CssMm(bounds.Y - clip.Y)};width:{CssMm(bounds.Width)};height:{CssMm(bounds.Height)};{style}\"></div>");
        html.AppendLine("</div>");
    }

    private static void AppendClippedContent(StringBuilder html, Rect bounds, Rect clip, string innerClass, string innerStyle, string innerHtml)
    {
        if (IsSameRect(bounds, clip))
        {
            html.AppendLine($"<div class=\"vitem {innerClass}\" style=\"left:{CssMm(bounds.X)};top:{CssMm(bounds.Y)};{innerStyle}\">{innerHtml}</div>");
            return;
        }

        html.AppendLine($"<div class=\"vitem\" style=\"left:{CssMm(clip.X)};top:{CssMm(clip.Y)};width:{CssMm(clip.Width)};height:{CssMm(clip.Height)}\">");
        html.AppendLine($"<div class=\"vinner {innerClass}\" style=\"left:{CssMm(bounds.X - clip.X)};top:{CssMm(bounds.Y - clip.Y)};{innerStyle}\">{innerHtml}</div>");
        html.AppendLine("</div>");
    }

    private static void AppendClippedSvg(StringBuilder html, Rect bounds, Rect clip, double viewWidth, double viewHeight, string svgContent)
    {
        var viewBox = $"0 0 {viewWidth.ToString("0.###", CultureInfo.InvariantCulture)} {viewHeight.ToString("0.###", CultureInfo.InvariantCulture)}";
        if (IsSameRect(bounds, clip))
        {
            html.AppendLine($"<svg class=\"vsvg\" style=\"left:{CssMm(bounds.X)};top:{CssMm(bounds.Y)};width:{CssMm(bounds.Width)};height:{CssMm(bounds.Height)}\" viewBox=\"{viewBox}\">{svgContent}</svg>");
            return;
        }

        html.AppendLine($"<div class=\"vitem\" style=\"left:{CssMm(clip.X)};top:{CssMm(clip.Y)};width:{CssMm(clip.Width)};height:{CssMm(clip.Height)}\">");
        html.AppendLine($"<svg class=\"vsvg\" style=\"left:{CssMm(bounds.X - clip.X)};top:{CssMm(bounds.Y - clip.Y)};width:{CssMm(bounds.Width)};height:{CssMm(bounds.Height)}\" viewBox=\"{viewBox}\">{svgContent}</svg>");
        html.AppendLine("</div>");
    }

    private static bool IsSameRect(Rect a, Rect b)
    {
        const double tolerance = 0.01;
        return Math.Abs(a.X - b.X) < tolerance
            && Math.Abs(a.Y - b.Y) < tolerance
            && Math.Abs(a.Width - b.Width) < tolerance
            && Math.Abs(a.Height - b.Height) < tolerance;
    }

    private static Point TransformVectorPoint(Visual visual, FrameworkElement root, Point point)
    {
        try
        {
            return visual.TransformToAncestor(root).Transform(point);
        }
        catch
        {
            return point;
        }
    }

    private static string CssMm(double value) => $"{value.ToString("0.###", CultureInfo.InvariantCulture)}mm";

    private static string BrushToCss(System.Windows.Media.Brush? brush)
    {
        if (brush is SolidColorBrush solid)
        {
            var color = solid.Color;
            var alpha = color.A / 255d * solid.Opacity;
            if (alpha <= 0.001) return "transparent";
            return alpha >= 0.999
                ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"rgba({color.R},{color.G},{color.B},{alpha.ToString("0.###", CultureInfo.InvariantCulture)})";
        }

        return "transparent";
    }

    private static string ImageSourceToDataUri(ImageSource? source)
    {
        if (source is BitmapImage bitmapImage && bitmapImage.UriSource is not null)
        {
            return bitmapImage.UriSource.IsAbsoluteUri
                ? bitmapImage.UriSource.AbsoluteUri
                : new Uri(bitmapImage.UriSource.ToString(), UriKind.RelativeOrAbsolute).ToString();
        }
        if (source is BitmapSource bitmapSource)
        {
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
            }
            catch
            {
                return "";
            }
        }

        return "";
    }
}
