using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Borevexa.Prescan.App.Services;

public sealed class GisSidebarBuilder
{
    public void AddBulkButtons(Panel parent, string onLabel, string offLabel, RoutedEventHandler clickHandler)
    {
        var buttons = new UniformGrid { Columns = 2, Margin = new Thickness(5, 6, 5, 10), MinHeight = 34 };
        AddBulkButton(buttons, onLabel, true, clickHandler);
        AddBulkButton(buttons, offLabel, false, clickHandler);
        parent.Children.Add(buttons);
    }

    public void AddBaseLayerRadio(
        Panel parent,
        string label,
        string layerId,
        string selectedLayerId,
        RoutedEventHandler clickHandler)
    {
        var selected = selectedLayerId == layerId;
        var typeLabel = BaseLayerTypeLabel(layerId);
        var row = new Button
        {
            Tag = layerId,
            MinHeight = 28,
            Background = selected ? Brush("#EEF2F5") : Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 1, 0, 1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(row, $"{label} {(selected ? "actief" : "niet actief")}");
        AutomationProperties.SetHelpText(row, $"Ondergrondlaag: {typeLabel}");
        row.Click += clickHandler;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new Ellipse
        {
            Width = 14,
            Height = 14,
            Stroke = selected ? Brush("#3F4750") : Brush("#DEE6EA"),
            Fill = selected ? Brush("#3F4750") : Brushes.White,
            StrokeThickness = 2,
            Margin = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        var text = new TextBlock
        {
            Text = label,
            Foreground = Brush("#071422"),
            FontSize = 11,
            FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var typeBadge = CreateLayerTypeBadge(typeLabel);
        Grid.SetColumn(typeBadge, 2);
        grid.Children.Add(typeBadge);

        if (selected)
        {
            var activeText = new TextBlock
            {
                Text = "actief",
                Foreground = Brush("#3F4750"),
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            Grid.SetColumn(activeText, 3);
            grid.Children.Add(activeText);
        }

        row.Content = grid;
        parent.Children.Add(row);
    }

    public void AddLayerToggle(
        Panel parent,
        string label,
        string tag,
        string typeLabel,
        bool visible,
        Thickness margin,
        RoutedEventHandler clickHandler,
        double fontSize = 11)
    {
        var row = CreateLayerToggleRow(label, tag, typeLabel, visible, margin, fontSize);
        row.Click += clickHandler;
        parent.Children.Add(row);
    }

    public static string BaseLayerTypeLabel(string layerId) => layerId switch
    {
        "pdok-brt" or "pdok-gray" or "pdok-pastel" or "pdok-bgt-pastel" => "PDOK tiles",
        "pdok-aerial" => "WMTS luchtfoto",
        "esri-topo-rd" or "esri-open-topo" or "esri-aerial" => "WMS",
        _ => "kaartlaag"
    };

    public static string OverlayTypeLabel(string overlayId) => overlayId switch
    {
        "parcels" => "PDOK WFS",
        "buildings" or "addresses" => "BAG API",
        "baseMap" => "achtergrond",
        "bgt" => "Kadaster ZIP/GML",
        "bagImport" => "BAG ZIP/GML",
        "ahn4Dtm" or "ahn4Dsm" => "AHN raster",
        "broGeomorphology"
            or "broSoilMap"
            or "broGroundwaterGhg"
            or "broGroundwaterGlg"
            or "broGroundwaterGvg"
            or "broGroundwaterGt"
            or "broGroundwaterDocumentation" => "PDOK WMS",
        "klic" or "klicBuffer" => "KLIC GML/ZIP",
        "designImport" => "DXF/GML",
        "customImport" => "ZIP/DXF/GML",
        "boreTrace" or "boreTraceNumbers" or "boreTraceLengths" => "project JSON",
        "profileTracePoints" => "profiel JSON",
        "machines" => "project JSON",
        _ => "overlay"
    };

    public static string BgtSurfaceFilterLabel(string surface)
    {
        return surface.ToLowerInvariant() switch
        {
            "asfalt" => "BGT asfalt/verharding",
            "groenstrook" => "BGT groenvoorziening",
            "water" => "BGT water",
            "onverhard" => "BGT onverhard",
            "bebouwing" => "BGT bebouwing",
            "spoor" => "BGT spoor",
            _ => "BGT overig"
        };
    }

    private static void AddBulkButton(Panel parent, string label, bool visible, RoutedEventHandler clickHandler)
    {
        var button = new Button
        {
            Content = label,
            Tag = visible,
            Height = 32,
            Margin = new Thickness(0, 0, 5, 0),
            Padding = new Thickness(6, 0, 6, 0),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Background = visible ? Brush("#DDF4EA") : Brushes.White,
            Foreground = Brush("#071422"),
            BorderBrush = Brush("#DEE6EA"),
            BorderThickness = new Thickness(1)
        };
        button.Click += clickHandler;
        parent.Children.Add(button);
    }

    private static Button CreateLayerToggleRow(string label, string tag, string typeLabel, bool visible, Thickness margin, double fontSize)
    {
        var row = new Button
        {
            Tag = tag,
            MinHeight = 26,
            Margin = margin,
            Padding = new Thickness(0, 1, 0, 1),
            Background = visible ? Brush("#F8FAFB") : Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(row, $"{label} {(visible ? "aan" : "uit")}");
        AutomationProperties.SetHelpText(row, typeLabel);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new Border
        {
            Width = 12,
            Height = 12,
            BorderBrush = visible ? Brush("#3F4750") : Brush("#94A3B8"),
            BorderThickness = new Thickness(1),
            Background = visible ? Brush("#3F4750") : Brushes.White,
            Margin = new Thickness(2, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        var text = new TextBlock
        {
            Text = label,
            Foreground = Brush("#071422"),
            FontSize = fontSize,
            FontWeight = visible ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var badge = CreateLayerTypeBadge(typeLabel);
        Grid.SetColumn(badge, 2);
        grid.Children.Add(badge);

        var state = CreateLayerStateBadge(visible);
        Grid.SetColumn(state, 3);
        grid.Children.Add(state);

        row.Content = grid;
        return row;
    }

    private static Border CreateLayerTypeBadge(string text)
    {
        return new Border
        {
            Background = Brush("#EEF6FA"),
            BorderBrush = Brush("#D8E8EF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(8, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brush("#4D7CA4"),
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold
            }
        };
    }

    private static Border CreateLayerStateBadge(bool visible)
    {
        return new Border
        {
            Background = visible ? Brush("#DDF4EA") : Brushes.White,
            BorderBrush = visible ? Brush("#B7DFC8") : Brush("#DEE6EA"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(7, 1, 7, 1),
            Margin = new Thickness(2, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = visible ? "aan" : "uit",
                Foreground = Brush("#071422"),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold
            }
        };
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));
}
