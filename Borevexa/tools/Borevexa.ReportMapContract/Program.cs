using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Borevexa.App.Services;

var repoRoot = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
    ?? FindRepoRoot(AppContext.BaseDirectory);

var report = new StringWriter();
report.WriteLine("Borevexa rapportkaart contract");
report.WriteLine($"Repo: {repoRoot}");
report.WriteLine();

var success = true;
success &= CheckMapCaptureValidation(report);
success &= CheckMapLibreCaptureContract(repoRoot, report);
success &= CheckMapMessageContract(repoRoot, report);

Console.WriteLine(report.ToString());
return success ? 0 : 1;

// Berichtencontract C# <-> MapLibre (fase 4, 07-07-2026): elk berichttype dat de
// WPF-kant naar de kaart stuurt moet een handler in step3-map.html hebben, en elk
// bericht dat de kaart terugstuurt een case in de C#-berichtafhandeling. Dit vangt
// typefouten en vergeten handlers af die anders geluidloos falen.
static bool CheckMapMessageContract(string repoRoot, TextWriter report)
{
    var appDir = Path.Combine(repoRoot, "Borevexa.App");
    var mapHtmlPath = Path.Combine(appDir, "Assets", "MapLibre", "step3-map.html");
    if (!File.Exists(mapHtmlPath) || !Directory.Exists(appDir)) return false;

    var html = File.ReadAllText(mapHtmlPath);
    var code = string.Join("\n", Directory.GetFiles(appDir, "*.cs", SearchOption.TopDirectoryOnly)
        .Concat(Directory.GetFiles(Path.Combine(appDir, "Services"), "*.cs"))
        .Select(File.ReadAllText));

    // C# -> kaart: {"type":"x"} in JSON-strings en new { type = "x" } payloads.
    // Kaartberichttypen zijn camelCase; PascalCase-treffers zijn GeoJSON-types
    // (Feature, LineString, ...) en horen niet bij het berichtencontract.
    var sentToMap = new SortedSet<string>(StringComparer.Ordinal);
    foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                 code, "\\\\\"type\\\\\":\\\\\"([a-z][A-Za-z]*)\\\\\""))
        sentToMap.Add(match.Groups[1].Value);
    foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                 code, "type = \"([a-z][A-Za-z]*)\""))
        sentToMap.Add(match.Groups[1].Value);

    // kaart-handlers: message.type === "x"
    var handledByMap = new SortedSet<string>(StringComparer.Ordinal);
    foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                 html, "message\\.type === \"([A-Za-z]+)\""))
        handledByMap.Add(match.Groups[1].Value);

    // kaart -> C#: send({ type: "x" ... })
    var sentByMap = new SortedSet<string>(StringComparer.Ordinal);
    foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                 html, "send\\(\\{\\s*type: \"([A-Za-z]+)\""))
        sentByMap.Add(match.Groups[1].Value);

    // C#-handlers: case "x": in de WebMessage-afhandeling.
    var handledByApp = new SortedSet<string>(StringComparer.Ordinal);
    foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                 code, "case \"([A-Za-z]+)\":"))
        handledByApp.Add(match.Groups[1].Value);

    report.WriteLine("Berichtencontract C# <-> MapLibre");
    var ok = true;

    var missingInMap = sentToMap.Where(type => !handledByMap.Contains(type)).ToList();
    if (missingInMap.Count == 0)
    {
        report.WriteLine($"  OK: alle {sentToMap.Count} C#->kaart berichttypen hebben een handler in step3-map.html");
    }
    else
    {
        ok = false;
        report.WriteLine($"  FOUT: C#->kaart zonder handler in step3-map.html: {string.Join(", ", missingInMap)}");
    }

    var missingInApp = sentByMap.Where(type => !handledByApp.Contains(type)).ToList();
    if (missingInApp.Count == 0)
    {
        report.WriteLine($"  OK: alle {sentByMap.Count} kaart->C# berichttypen hebben een case in de app");
    }
    else
    {
        ok = false;
        report.WriteLine($"  FOUT: kaart->C# zonder case in de app: {string.Join(", ", missingInApp)}");
    }

    report.WriteLine();
    return ok;
}

static bool CheckMapCaptureValidation(TextWriter report)
{
    var dir = Path.Combine(Path.GetTempPath(), "Borevexa.ReportMapContract", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var yellow = Path.Combine(dir, "yellow-placeholder.png");
        var varied = Path.Combine(dir, "varied-map.png");
        WriteSolidPng(yellow, 640, 420, Colors.Yellow);
        WriteVariedMapPng(varied, 640, 420);

        var yellowValidation = ReportPreviewService.ValidateMapCapture(yellow);
        var variedValidation = ReportPreviewService.ValidateMapCapture(varied);

        report.WriteLine("Capturevalidatie");
        report.WriteLine($"  geel placeholdervlak: {(yellowValidation.IsUsable ? "FOUT" : "OK")} ({yellowValidation.Reason})");
        report.WriteLine($"  kaartachtige capture: {(variedValidation.IsUsable ? "OK" : "FOUT")} ({variedValidation.Reason})");
        report.WriteLine();

        return !yellowValidation.IsUsable && variedValidation.IsUsable;
    }
    finally
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}

static bool CheckMapLibreCaptureContract(string repoRoot, TextWriter report)
{
    var appDir = Path.Combine(repoRoot, "Borevexa.App");
    var mapHtml = Path.Combine(appDir, "Assets", "MapLibre", "step3-map.html");
    // MainWindow is sinds de fase 3-opsplitsing verdeeld over meerdere partial-
    // bestanden (MainWindow.*.cs); het contract geldt voor het geheel.
    var mainWindowPartials = Directory.Exists(appDir)
        ? Directory.GetFiles(appDir, "MainWindow*.cs", SearchOption.TopDirectoryOnly)
        : Array.Empty<string>();
    var ok = true;

    report.WriteLine("Broncontract");
    if (!File.Exists(mapHtml))
    {
        report.WriteLine("  FOUT: step3-map.html ontbreekt.");
        return false;
    }

    var html = File.ReadAllText(mapHtml);
    ok &= Contains(report, html, "prepareReportCapture", "MapLibre heeft prepareReportCapture hook");
    ok &= Contains(report, html, "reportCaptureMode", "MapLibre ondersteunt reportCaptureMode");
    ok &= Contains(report, html, "capture-controls-hidden", "MapLibre kan kaartknoppen verbergen voor capture");
    ok &= Contains(report, html, "boreline-map-mode", "MapLibre heeft afgeschermde 3.1 boorlijnmodus");
    ok &= Contains(report, html, "borelineBaseStyles", "MapLibre beperkt 3.1 ondergronden tot PDOK BRT/luchtfoto");
    ok &= Contains(report, html, "traceSmoothVisual = !traceSmoothVisual", "Boorlijn curveweergave blijft niet-destructief");

    if (mainWindowPartials.Length == 0)
    {
        report.WriteLine("  FOUT: MainWindow*.cs bestanden ontbreken.");
        return false;
    }

    var code = string.Join("\n", mainWindowPartials.Select(File.ReadAllText));
    ok &= Contains(report, code, "CaptureLiveMapForReportPreviewAsync", "WPF capture gebruikt live WebView2-capture");
    ok &= Contains(report, code, "GetLiveMapReportPreviewImagePath", "Rapportpreview leest opgeslagen live capture");
    // Geactualiseerd 07-07-2026: de bouwer heet inmiddels CreateLiveMapReportImageCard
    // en de rapporttekst beschrijft het 1-op-1/vastzet-contract met andere woorden.
    ok &= Contains(report, code, "CreateLiveMapReportImageCard(", "Rapport toont live/vastgezette kaartcapture via kaartkaart-bouwer");
    ok &= Contains(report, code, "Dit kaartbeeld volgt exact de gekozen kaartuitsnede in de app", "Rapporttekst legt live-capture contract vast");
    ok &= Contains(report, code, "EnsureFreshMapReportCaptureAsync", "Preview/export capturet eerst vers kaartbeeld");
    ok &= Contains(report, code, "FreezeLockedMapImage", "Vastgezet rapportbeeld wordt bevroren in eigen bestand");
    ok &= Contains(report, code, "Bedieningsknoppen en tekengereedschap worden bij de capture verborgen", "Rapportcapture verbergt kaartbediening en tekengereedschap");
    ok &= Contains(report, code, "IsBorelineStep(_selectedStep?.Number)", "Boorlijn-overlay wordt ook in stap 3 ingeschakeld");
    ok &= Contains(report, code, "GetLiveMapReportPreviewImagePath(3)", "Stap 3.1 gebruikt opgeslagen kaartcapture uit stap 3");
    report.WriteLine();
    return ok;
}

static bool Contains(TextWriter report, string text, string needle, string label)
{
    var ok = text.Contains(needle, StringComparison.Ordinal);
    report.WriteLine($"  {(ok ? "OK" : "FOUT")}: {label}");
    return ok;
}

static void WriteSolidPng(string path, int width, int height, Color color)
{
    var visual = new DrawingVisual();
    using (var context = visual.RenderOpen())
    {
        context.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, width, height));
    }

    SavePng(path, visual, width, height);
}

static void WriteVariedMapPng(string path, int width, int height)
{
    var visual = new DrawingVisual();
    using (var context = visual.RenderOpen())
    {
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(235, 242, 241)), null, new Rect(0, 0, width, height));
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(215, 233, 223)), null, new Rect(20, 270, 260, 110));
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(226, 232, 240)), null, new Rect(330, 60, 210, 145));
        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(120, 183, 213)), 18),
            Geometry.Parse("M 0 142 C 126 156, 236 145, 372 164 S 525 152, 640 178"));
        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(255, 255, 255)), 8),
            Geometry.Parse("M 90 0 C 138 70, 165 150, 204 228 S 232 360, 260 420"));
        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(225, 29, 72)), 5),
            Geometry.Parse("M 80 350 L 210 310 L 375 265 L 540 225"));
        context.DrawEllipse(new SolidColorBrush(Color.FromRgb(15, 23, 42)), null, new Point(80, 350), 5, 5);
        context.DrawEllipse(new SolidColorBrush(Color.FromRgb(15, 23, 42)), null, new Point(540, 225), 5, 5);
    }

    SavePng(path, visual, width, height);
}

static void SavePng(string path, Visual visual, int width, int height)
{
    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = File.Create(path);
    encoder.Save(stream);
}

static string FindRepoRoot(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Borevexa.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return Directory.GetCurrentDirectory();
}
