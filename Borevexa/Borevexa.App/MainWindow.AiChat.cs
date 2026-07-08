using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;
using Borevexa.App.Models;
using Borevexa.App.Reports.Blocks;
using Borevexa.App.Services;
using Borevexa.Cad;
using Borevexa.Core.Models;
using Borevexa.Core.Services;
using Borevexa.Geo;
using Docnet.Core;
using Docnet.Core.Converters;
using Docnet.Core.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using DrawingImageLockMode = System.Drawing.Imaging.ImageLockMode;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangle = System.Drawing.Rectangle;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using NtsGeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using NtsLineString = NetTopologySuite.Geometries.LineString;
using NtsLinearRing = NetTopologySuite.Geometries.LinearRing;
using NtsPolygon = NetTopologySuite.Geometries.Polygon;
using UglyToad.PdfPig;

namespace Borevexa.App;

// AI-chat en kennisbibliotheek.
// Fase 3-opsplitsing (07-07-2026) van MainWindow.xaml.cs; gedrag ongewijzigd.

public partial class MainWindow
{
    private string BuildAiAnalysisContext(IReadOnlyList<ProjectMapLayer> layers, IReadOnlyList<ProjectDocumentEntry> docs, IReadOnlyList<string> themes)
    {
        var boring = ComputeBoring();
        var selectedMachine = Machines.FirstOrDefault(machine => machine.Id == _selectedMachineId);
        var sb = new StringBuilder();
        sb.AppendLine("Projectcontext Borevexa Prescan");
        sb.AppendLine($"Project: {_selectedProject?.Name ?? "-"}");
        sb.AppendLine($"Opdrachtgever: {_selectedProject?.Client ?? "-"}");
        sb.AppendLine($"Locatie: {_selectedProject?.Location ?? "-"}");
        sb.AppendLine($"Status: {_selectedProject?.Status ?? "-"}");
        sb.AppendLine($"Boordiameter: Ø{boring.BoringDiameter:0} mm");
        sb.AppendLine($"Productbundel: Ø{boring.BundleDiameter:0} mm");
        sb.AppendLine($"Producten/mantelbuizen: {_boringItems.Count}");
        foreach (var item in _boringItems)
        {
            sb.AppendLine($"- {item.Label}: Ø{item.OutsideDiameter:0} mm, type {item.Type}, kleur {item.Color}");
            foreach (var cable in item.Contents)
            {
                sb.AppendLine($"  - kabel/product: {cable.Label}, Ø{cable.OutsideDiameter:0} mm");
            }
        }

        if (selectedMachine is not null)
        {
            var recommendation = BuildSelectedMachineRecommendation(boring.BoringDiameter);
            sb.AppendLine($"Machine: {selectedMachine.Brand} {selectedMachine.Model}, max Ø{selectedMachine.MaxBoring:0} mm, kracht {FormatMachineForceValue(selectedMachine)}, stangenrek {selectedMachine.RodsMeters:0} m, advies {recommendation.Label.ToLowerInvariant()}: {recommendation.Reason}");
        }
        else
        {
            sb.AppendLine("Machine: niet gekozen");
        }

        sb.AppendLine($"Lokale projectbestanden: {_projectFiles.Count}");
        sb.AppendLine($"Documenten/bijlagen: {docs.Count}");
        sb.AppendLine($"Kaartlagen: {layers.Count}");
        sb.AppendLine($"Geometrieen: {layers.Sum(layer => layer.FeatureCollection.Features.Count)}");
        sb.AppendLine("KLIC/BGT thema's:");
        if (themes.Count > 0)
        {
            foreach (var theme in themes.Take(24))
            {
                sb.AppendLine($"- {theme}");
            }
        }
        else
        {
            sb.AppendLine("- Geen thema's gevonden");
        }

        var bgtCounts = layers
            .SelectMany(layer => layer.FeatureCollection.Features)
            .Select(feature => feature.Properties.TryGetValue("surface", out var surface) ? surface?.ToString() : null)
            .Where(surface => !string.IsNullOrWhiteSpace(surface))
            .GroupBy(surface => surface!, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}: {group.Count()}")
            .Take(12)
            .ToList();
        if (bgtCounts.Count > 0)
        {
            sb.AppendLine("BGT oppervlakken:");
            foreach (var item in bgtCounts)
            {
                sb.AppendLine($"- {item}");
            }
        }

        return sb.ToString().Trim();
    }

    private string BuildKnowledgeGuidanceForText(string subject, string details)
    {
        var documents = ReadKnowledgeDocuments()
            .Where(document => !string.IsNullOrWhiteSpace(document.ExtractedText))
            .Take(MaxKnowledgeDocumentsForPrompt)
            .ToList();
        if (documents.Count == 0) return "";

        var terms = BuildKnowledgeSearchTerms(subject, details).ToList();
        var matchedSentences = documents
            .SelectMany(document => SelectKnowledgeSentences(document, terms))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Text.Length)
            .Take(2)
            .Select(item => item.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        var names = string.Join(", ", documents.Select(document => System.IO.Path.GetFileNameWithoutExtension(document.DisplayName)).Take(3));
        var focus = terms.Count == 0 ? "de technische randvoorwaarden" : string.Join(", ", terms.Take(5));
        var builder = new StringBuilder();
        builder.Append($"De gekoppelde documentatie ({names}) is gebruikt als referentiekader voor {subject.ToLower(CultureInfo.CurrentCulture)}. ");
        builder.Append($"De beoordeling houdt daardoor nadrukkelijk rekening met {focus}.");
        if (matchedSentences.Count > 0)
        {
            builder.Append(" Relevante aandachtspunten uit de documentatie zijn verwerkt in de controle op uitgangspunten, risico's en vervolgstappen.");
        }

        return builder.ToString();
    }

    private static IEnumerable<string> BuildKnowledgeSearchTerms(string subject, string details)
    {
        var source = $"{subject} {details}";
        var tokens = Regex.Matches(source.ToLowerInvariant(), @"[a-z0-9\-]{4,}")
            .Select(match => match.Value.Trim('-'))
            .Where(token => token.Length >= 4)
            .Where(token => token is not "deze" and not "voor" and not "project" and not "status" and not "rapportdata" and not "belangrijkste")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        if (source.Contains("boorlijn", StringComparison.OrdinalIgnoreCase) || source.Contains("hdd", StringComparison.OrdinalIgnoreCase))
        {
            tokens.AddRange(["hdd", "boring", "trac", "boorlijn"]);
        }
        if (source.Contains("klic", StringComparison.OrdinalIgnoreCase) || source.Contains("kabel", StringComparison.OrdinalIgnoreCase) || source.Contains("leiding", StringComparison.OrdinalIgnoreCase))
        {
            tokens.AddRange(["klic", "kabel", "leiding", "afstand"]);
        }
        if (source.Contains("oppervlak", StringComparison.OrdinalIgnoreCase) || source.Contains("bgt", StringComparison.OrdinalIgnoreCase) || source.Contains("maaiveld", StringComparison.OrdinalIgnoreCase))
        {
            tokens.AddRange(["bgt", "maaiveld", "verharding", "hoogte"]);
        }
        if (source.Contains("grond", StringComparison.OrdinalIgnoreCase) || source.Contains("sondering", StringComparison.OrdinalIgnoreCase))
        {
            tokens.AddRange(["grond", "sondering", "geotechnisch"]);
        }

        return tokens
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private string BuildLocalAiAnalysis(IReadOnlyList<ProjectMapLayer> layers, IReadOnlyList<ProjectDocumentEntry> docs, IReadOnlyList<string> themes)
    {
        var sb = new StringBuilder();
        var stepNumber = _selectedStep?.Number ?? 0;
        var klicLayers = layers.Where(layer => layer.Type.Equals("KLIC", StringComparison.OrdinalIgnoreCase)).ToList();
        var bgtLayers = layers.Where(layer => layer.Type.Equals("BGT", StringComparison.OrdinalIgnoreCase)).ToList();
        var bagLayers = layers.Where(layer => layer.Type.Equals("BAG", StringComparison.OrdinalIgnoreCase)).ToList();
        var designLayers = layers.Where(layer => !layer.Type.Equals("KLIC", StringComparison.OrdinalIgnoreCase)
                                                 && !layer.Type.Equals("BGT", StringComparison.OrdinalIgnoreCase)
                                                 && !layer.Type.Equals("BAG", StringComparison.OrdinalIgnoreCase)).ToList();

        sb.AppendLine(IsBorelineStep(stepNumber) ? "AI analyse - Boorlijn" : "AI analyse - Ontwerp bekijken");
        sb.AppendLine();
        sb.AppendLine($"Importbestanden: {_projectFiles.Count}");
        sb.AppendLine($"Kaartlagen: {layers.Count} ({layers.Sum(layer => layer.FeatureCollection.Features.Count)} geometrieen)");
        sb.AppendLine($"KLIC lagen: {klicLayers.Count}");
        sb.AppendLine($"BGT lagen: {bgtLayers.Count}");
        sb.AppendLine($"BAG/Kadaster lagen: {bagLayers.Count}");
        sb.AppendLine($"Ontwerplagen: {designLayers.Count}");
        sb.AppendLine($"KLIC documenten: {docs.Count}");

        if (IsBorelineStep(stepNumber))
        {
            var points = _currentBoreTracePoints.Count;
            var length = TraceLengthMeters(_currentBoreTracePoints);
            sb.AppendLine($"Boorlijnpunten: {points}");
            sb.AppendLine($"Boorlijnlengte: {length:0.#} m");
        }

        sb.AppendLine();
        sb.AppendLine("Aandachtspunten:");
        if (klicLayers.Count == 0) sb.AppendLine("- Geen KLIC import zichtbaar: controleer stap 2 voordat je de boorlijn definitief maakt.");
        if (bgtLayers.Count == 0) sb.AppendLine("- Geen BGT import zichtbaar: oppervlakteanalyse wordt beperkt.");
        if (bagLayers.Count == 0) sb.AppendLine("- Geen BAG/Kadaster import zichtbaar: object- en perceelcontrole is beperkt.");
        if (designLayers.Count == 0) sb.AppendLine("- Geen ontwerplagen zichtbaar: ontwerpcontrole kan niet volledig worden uitgevoerd.");
        if (docs.Count == 0) sb.AppendLine("- Geen KLIC documenten gevonden: bijlagen/rapportage zijn nog niet compleet.");
        if (IsBorelineStep(stepNumber) && _currentBoreTracePoints.Count < 2) sb.AppendLine("- Boorlijn heeft minder dan 2 punten: teken minimaal een intrede- en uittredepunt.");
        if (themes.Count > 0) sb.AppendLine($"- KLIC thema's gevonden: {string.Join(", ", themes.Take(8))}.");

        sb.AppendLine();
        sb.AppendLine("Vervolg:");
        sb.AppendLine("- Zet overbodige lagen uit en controleer kruisingen rond de boorlijn.");
        sb.AppendLine("- Sla de kaartpositie en boorlijn op voordat je naar analyse/profiel gaat.");
        sb.AppendLine("- Open KLIC/BGT informatie door objecten op de kaart te selecteren.");

        return sb.ToString().Trim();
    }

    private Border CreateKnowledgeDocumentCard(KnowledgeDocumentRecord document)
    {
        var card = new Border
        {
            Background = Brush("#F8FAFB"),
            BorderBrush = Brush("#E3ECF3"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var panel = new DockPanel { LastChildFill = true };
        var removeButton = new Button
        {
            Content = "x",
            Tag = document.Id,
            Width = 26,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            Background = Brushes.White,
            Foreground = Brush("#587080"),
            BorderBrush = Brush("#CBD5E1"),
            ToolTip = "Documentatie verwijderen"
        };
        removeButton.Click += RemoveKnowledgeDocument_OnClick;
        DockPanel.SetDock(removeButton, Dock.Right);
        panel.Children.Add(removeButton);

        var textPanel = new StackPanel();
        textPanel.Children.Add(new TextBlock
        {
            Text = document.DisplayName,
            Foreground = Brush("#071422"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = $"{FormatFileSize(document.SizeBytes)} - {document.ImportStatus}",
            Foreground = Brush("#587080"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });
        panel.Children.Add(textPanel);
        card.Child = panel;
        return card;
    }

    private static string ExtractKnowledgeDocumentText(string path)
    {
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (extension is ".txt" or ".md" or ".csv")
            {
                return NormalizeKnowledgeText(File.ReadAllText(path));
            }

            if (extension == ".docx")
            {
                return ExtractDocxText(path);
            }

            if (extension == ".pdf")
            {
                return ExtractPdfText(path);
            }

            return "";
        }
        catch
        {
            return "";
        }
    }

    private string GetKnowledgeLibraryDirectory()
    {
        if (_selectedProject is null) return "";
        var directory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Borevexa",
            "KnowledgeLibrary",
            _selectedProject.Id.ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private KnowledgeDocumentRecord ImportKnowledgeDocument(string sourcePath)
    {
        if (_selectedProject is null) throw new InvalidOperationException("Geen project actief.");
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Document niet gevonden.", sourcePath);

        var id = Guid.NewGuid();
        var extension = System.IO.Path.GetExtension(sourcePath);
        var safeName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{id:N}_{ToSafeFileName(System.IO.Path.GetFileNameWithoutExtension(sourcePath))}{extension}";
        var localPath = System.IO.Path.Combine(GetKnowledgeLibraryDirectory(), safeName);
        File.Copy(sourcePath, localPath, overwrite: true);

        var text = ExtractKnowledgeDocumentText(localPath);
        var info = new FileInfo(localPath);
        return new KnowledgeDocumentRecord
        {
            Id = id,
            DisplayName = System.IO.Path.GetFileName(sourcePath),
            SourcePath = sourcePath,
            LocalPath = localPath,
            SizeBytes = info.Length,
            ImportedAt = DateTimeOffset.Now,
            IndexedAt = DateTimeOffset.Now,
            ExtractedText = TruncateKnowledgeText(text, MaxKnowledgeDocumentTextChars),
            ImportStatus = string.IsNullOrWhiteSpace(text)
                ? "Bestand gekoppeld; er kon geen tekst worden geindexeerd."
                : $"Tekst geindexeerd ({text.Length:N0} tekens)."
        };
    }

    private static string NormalizeKnowledgeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var normalized = Regex.Replace(text, @"[ \t]+", " ");
        normalized = Regex.Replace(normalized, @"\s*\n\s*", "\n");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }

    private IReadOnlyList<KnowledgeDocumentRecord> ReadKnowledgeDocuments()
    {
        if (_selectedProject is null) return [];
        var json = _projects.GetStepData(_selectedProject.Id, 0, KnowledgeLibraryDataKey);
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<KnowledgeDocumentRecord>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void RenderAiAnalysisPanel()
    {
        AiAnalysisPanel.Children.Add(new TextBlock
        {
            Text = "Analyseer de zichtbare kaartlagen, imports, boorlijn en aandachtspunten.",
            Foreground = Brush("#334155"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        AiAnalysisPanel.Children.Add(new Button
        {
            Content = "Analyse uitvoeren",
            Height = 32,
            Background = Brush("#3F4750"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold
        });
        if (AiAnalysisPanel.Children[^1] is Button button)
        {
            button.Click += AiAnalysis_OnClick;
        }

        AiAnalysisPanel.Children.Add(new TextBlock
        {
            Text = "AI-vraag",
            Foreground = Brush("#071422"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 5)
        });
        _aiQuestionInput = new TextBox
        {
            MinHeight = 74,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderBrush = Brush("#BFD7F1"),
            Padding = new Thickness(7),
            FontSize = 11,
            ToolTip = "Typ hier je vraag over deze stap of het project."
        };
        AiAnalysisPanel.Children.Add(_aiQuestionInput);
        var askButton = new Button
        {
            Content = "Vraag beantwoorden",
            Height = 32,
            Background = Brush("#3F4750"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 7, 0, 0)
        };
        askButton.Click += AskGroq_OnClick;
        AiAnalysisPanel.Children.Add(askButton);

        _sidebarAiAnalysisText = new TextBlock
        {
            Text = "Nog geen analyse uitgevoerd.",
            Foreground = Brush("#334155"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };
        AiAnalysisPanel.Children.Add(_sidebarAiAnalysisText);
    }

    private void RenderKnowledgeLibraryPanel()
    {
        if (_selectedProject is null)
        {
            KnowledgeLibraryStatusText.Text = "Open eerst een project om documentatie te koppelen.";
            KnowledgeDocumentsList.Children.Clear();
            return;
        }

        var documents = ReadKnowledgeDocuments();
        var indexed = documents.Count(document => !string.IsNullOrWhiteSpace(document.ExtractedText));
        KnowledgeLibraryStatusText.Text = documents.Count == 0
            ? "Nog geen documentatie gekoppeld. Voeg PDF, DOCX, TXT of Markdown toe."
            : $"{documents.Count} document(en) gekoppeld, {indexed} met tekstindex. Deze documentatie wordt meegenomen bij het genereren van inleidingen en voorwoord.";

        KnowledgeDocumentsList.Children.Clear();
        foreach (var document in documents.OrderByDescending(document => document.ImportedAt))
        {
            KnowledgeDocumentsList.Children.Add(CreateKnowledgeDocumentCard(document));
        }
    }

    private void SaveKnowledgeDocuments(IReadOnlyList<KnowledgeDocumentRecord> documents)
    {
        if (_selectedProject is null) return;
        SaveSelectedProjectStepData(0, KnowledgeLibraryDataKey, JsonSerializer.Serialize(documents, JsonOptions));
    }

    private static IEnumerable<(int Score, string Text)> SelectKnowledgeSentences(KnowledgeDocumentRecord document, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0 || string.IsNullOrWhiteSpace(document.ExtractedText)) yield break;

        var sentences = Regex.Split(document.ExtractedText, @"(?<=[\.\?!])\s+|\n+")
            .Select(sentence => Regex.Replace(sentence.Trim(), @"\s+", " "))
            .Where(sentence => sentence.Length is >= 50 and <= 380);

        foreach (var sentence in sentences)
        {
            var score = terms.Count(term => sentence.Contains(term, StringComparison.OrdinalIgnoreCase));
            if (score <= 0) continue;
            yield return (score, sentence);
        }
    }

    private void SetAiAnalysisOutput(string text)
    {
        AiAnalysisText.Text = text;
        if (_sidebarAiAnalysisText is not null)
        {
            _sidebarAiAnalysisText.Text = text;
        }
        OutputText.Text = text;
    }

    private static string TruncateKnowledgeText(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        return text.Length <= maxChars ? text : text[..maxChars].Trim();
    }
}
