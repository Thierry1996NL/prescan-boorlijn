using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Borevexa.Prescan.App.Models;
using Borevexa.Prescan.Core.Models;
using Borevexa.Prescan.Core.Services;

namespace Borevexa.Prescan.App;

public partial class MainWindow
{
    private List<ProjectMapLayer> BuildProjectMapLayers(IEnumerable<ProjectFileRecord> files)
    {
        return _mapLayerBuilder.Build(files, ReadProjectFeatures, LayerColor);
    }

    private void SyncKlicThemeStates(IEnumerable<ProjectMapLayer> layers)
    {
        foreach (var theme in layers
                     .Where(layer => !layer.Type.Equals("BGT", StringComparison.OrdinalIgnoreCase))
                     .SelectMany(layer => layer.FeatureCollection.Features)
                     .Select(feature => feature.Properties.TryGetValue("theme", out var theme) ? theme?.ToString() : null)
                     .Where(theme => !string.IsNullOrWhiteSpace(theme))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_klicThemeStates.ContainsKey(theme!))
            {
                _klicThemeStates[theme!] = true;
            }
        }
    }

    private static IEnumerable<ProjectDocumentEntry> BuildProjectDocumentEntries(IEnumerable<ProjectFileRecord> files) =>
        GisDocuments.BuildEntries(files);

    private static IEnumerable<object> BuildDocumentIndex(IEnumerable<ProjectDocumentEntry> docs) =>
        GisDocuments.BuildIndex(docs);

    private static string ReadDocumentContent(ProjectDocumentEntry doc, int maxChars) =>
        GisDocuments.ReadDocumentContent(doc, maxChars);

    private static bool IsTextDocumentType(string type) =>
        GisDocuments.IsTextDocumentType(type);

    private static Stream? OpenDocumentStream(ProjectDocumentEntry doc) =>
        GisDocuments.OpenDocumentStream(doc);

    private static IReadOnlyList<KlicContactRow> BuildKlicContactRows(IEnumerable<ProjectDocumentEntry> docs)
    {
        var rows = new List<KlicContactRow>();
        var xmlDocs = docs
            .Where(doc => doc.Type.Equals("XML", StringComparison.OrdinalIgnoreCase) || doc.Type.Equals("GML", StringComparison.OrdinalIgnoreCase))
            .Where(doc => IsLikelyKlicContactXmlName(doc.Name))
            .Take(4)
            .ToList();

        foreach (var doc in xmlDocs)
        {
            rows.AddRange(BuildKlicContactRowsFromXml(doc));
        }

        var pdfs = docs
            .Where(doc => doc.Type.Equals("PDF", StringComparison.OrdinalIgnoreCase))
            .Where(doc => IsLikelyKlicContactPdfName(doc.Name))
            .Where(doc => doc.SizeKb <= MaxKlicContactPdfBytes / 1024)
            .OrderBy(doc => doc.SizeKb)
            .Take(MaxKlicContactPdfDocs)
            .ToList();

        if (rows.Count == 0)
        {
            foreach (var doc in pdfs)
            {
                var text = ReadPdfDocumentText(doc);
                if (!LooksLikeKlicContactList(text, doc.Name))
                {
                    continue;
                }

                rows.AddRange(ParseKlicContactRows(text, doc.Name));
            }
        }

        return rows
            .GroupBy(row => $"{row.Code}|{row.NetworkOperator}|{row.Theme}|{row.Contact}|{row.Phone}|{row.Email}|{row.FaultPhone}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(row => KlicContactCodeOrder(row.Code))
            .ThenBy(row => row.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Theme, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsLikelyKlicContactXmlName(string name)
    {
        var file = System.IO.Path.GetFileName(name).ToLowerInvariant();
        return file.StartsWith("gi_", StringComparison.Ordinal) ||
               file.Contains("gebiedsinformatielevering", StringComparison.Ordinal);
    }

    private static IReadOnlyList<KlicContactRow> BuildKlicContactRowsFromXml(ProjectDocumentEntry doc)
    {
        try
        {
            using var stream = OpenDocumentStream(doc);
            if (stream is null) return [];
            var document = XDocument.Load(stream, LoadOptions.None);
            var managers = document.Descendants()
                .Where(element => element.Name.LocalName == "Beheerder")
                .Select(element =>
                {
                    var code = FirstDescendantText(element, "bronhoudercode");
                    if (string.IsNullOrWhiteSpace(code)) code = ExtractKlicOwnerCode(element.Attribute(XName.Get("id", "http://www.opengis.net/gml"))?.Value ?? "");
                    var name = element.Descendants()
                        .Where(descendant => descendant.Name.LocalName == "Organisatie")
                        .Select(descendant => FirstDescendantText(descendant, "naam"))
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
                    return (Code: code, Name: name);
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);

            var rows = new List<KlicContactRow>();
            foreach (var interest in document.Descendants().Where(element => element.Name.LocalName == "Belang"))
            {
                var code = ExtractKlicOwnerCode(interest.Attribute(XName.Get("id", "http://www.opengis.net/gml"))?.Value ?? "");
                if (string.IsNullOrWhiteSpace(code))
                {
                    code = interest.Descendants()
                        .Where(element => element.Name.LocalName == "netbeheerder")
                        .Select(element => ExtractKlicOwnerCode(GetHrefAttribute(element)))
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
                }
                if (string.IsNullOrWhiteSpace(code)) continue;

                var managerName = managers.TryGetValue(code, out var mappedName) && !string.IsNullOrWhiteSpace(mappedName)
                    ? mappedName
                    : code;
                var themes = interest.Elements()
                    .Where(element => element.Name.LocalName == "thema")
                    .Select(element => NormalizeKlicTheme(GetHrefAttribute(element)))
                    .Where(theme => !string.IsNullOrWhiteSpace(theme))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .DefaultIfEmpty("overig")
                    .ToList();
                var networkContact = ReadKlicXmlNetworkContact(interest);
                var damageContact = ReadKlicXmlDamageContact(interest);

                foreach (var theme in themes)
                {
                    rows.Add(new KlicContactRow(
                        code,
                        managerName,
                        KlicThemeLabel(theme),
                        networkContact.Name,
                        networkContact.Phone,
                        networkContact.Email,
                        damageContact.Phone,
                        doc.Name));
                }
            }

            return rows;
        }
        catch
        {
            return [];
        }
    }

    private static (string Name, string Phone, string Email) ReadKlicXmlNetworkContact(XElement interest)
    {
        var contact = interest.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "contactNetinformatie")?
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "AanvraagSoortContact");
        return ReadKlicXmlContact(contact);
    }

    private static (string Name, string Phone, string Email) ReadKlicXmlDamageContact(XElement interest)
    {
        var contact = interest.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "contactBeschadiging")?
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Contact");
        return ReadKlicXmlContact(contact);
    }

    private static (string Name, string Phone, string Email) ReadKlicXmlContact(XElement? contact)
    {
        if (contact is null) return ("-", "-", "-");
        var name = FirstDescendantText(contact, "naam");
        var phone = FirstDescendantText(contact, "telefoon");
        var email = FirstDescendantText(contact, "email");
        return (
            string.IsNullOrWhiteSpace(name) ? "-" : name,
            string.IsNullOrWhiteSpace(phone) ? "-" : phone,
            string.IsNullOrWhiteSpace(email) ? "-" : email);
    }

    private static string FirstDescendantText(XElement element, string localName) =>
        NormalizeContactCell(element.Descendants().FirstOrDefault(descendant => descendant.Name.LocalName == localName)?.Value ?? "");

    private static string GetHrefAttribute(XElement element) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "href")?.Value ?? "";

    private static string ExtractKlicOwnerCode(string value)
    {
        var match = Regex.Match(value, @"(?:^|[-_.])(?<code>[A-Z]{2}\d{3,5})(?:[-_.]|$)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["code"].Value.ToUpperInvariant() : "";
    }

    private static bool IsLikelyKlicContactPdfName(string name)
    {
        var file = System.IO.Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
        return file.StartsWith("li_", StringComparison.Ordinal) ||
               file.Contains("leveringsinformatie", StringComparison.Ordinal) ||
               file.Contains("netbeheerder", StringComparison.Ordinal) ||
               file.Contains("contact", StringComparison.Ordinal) ||
               file.Contains("klic", StringComparison.Ordinal);
    }

    private static ProjectDocumentEntry? FindKlicContactPdf(IEnumerable<ProjectDocumentEntry> docs)
    {
        return docs
            .Where(doc => doc.Type.Equals("PDF", StringComparison.OrdinalIgnoreCase))
            .Select(doc => (Doc: doc, Score: ScoreKlicContactPdf(doc)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Doc.SizeKb)
            .Select(item => item.Doc)
            .FirstOrDefault();
    }

    private static int ScoreKlicContactPdf(ProjectDocumentEntry doc)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(doc.Name).ToLowerInvariant();
        var path = (doc.ZipEntryName ?? doc.Name).ToLowerInvariant();
        var score = 0;
        if (Regex.IsMatch(name, @"^li[_-]?\d", RegexOptions.IgnoreCase)) score += 120;
        if (name.StartsWith("li_", StringComparison.Ordinal)) score += 80;
        if (path.Contains("/li_", StringComparison.Ordinal) || path.Contains("\\li_", StringComparison.Ordinal)) score += 80;
        if (name.Contains("leveringsinformatie", StringComparison.Ordinal)) score += 60;
        if (name.Contains("netbeheerder", StringComparison.Ordinal)) score += 50;
        if (name.Contains("contact", StringComparison.Ordinal)) score += 35;
        if (path.Contains("/bronnen/", StringComparison.Ordinal) || path.Contains("\\bronnen\\", StringComparison.Ordinal)) score -= 70;
        if (name.Contains("profielschets", StringComparison.Ordinal)) score -= 90;
        if (name.Contains("brief", StringComparison.Ordinal)) score -= 50;
        if (doc.SizeKb > 4096) score -= 20;
        return score;
    }

    private static int KlicContactCodeOrder(string code)
    {
        if (code.StartsWith("KL", StringComparison.OrdinalIgnoreCase)) return 0;
        if (code.StartsWith("GM", StringComparison.OrdinalIgnoreCase)) return 1;
        if (code.StartsWith("WS", StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    private static bool LooksLikeKlicContactList(string text, string fileName)
    {
        var haystack = $"{fileName}\n{text}".ToLowerInvariant();
        return haystack.Contains("netbeheerders met belangen", StringComparison.Ordinal) ||
               haystack.Contains("onderstaande netbeheerders hebben geleverd", StringComparison.Ordinal) ||
               haystack.Contains("contact netinformatie", StringComparison.Ordinal) ||
               haystack.Contains("schade/storing", StringComparison.Ordinal);
    }

    private static string ReadPdfDocumentText(ProjectDocumentEntry doc)
    {
        try
        {
            using var stream = OpenDocumentStream(doc);
            if (stream is null) return "";
            using var memory = new MemoryStream();
            CopyStreamLimited(stream, memory, MaxKlicContactPdfBytes);
            return ExtractPdfText(memory.ToArray());
        }
        catch
        {
            return "";
        }
    }

    private static void CopyStreamLimited(Stream input, Stream output, int maxBytes)
    {
        var buffer = new byte[81920];
        var remaining = maxBytes;
        while (remaining > 0)
        {
            var read = input.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read <= 0) break;
            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static string ExtractPdfText(byte[] bytes)
    {
        var fragments = new List<string>();
        AddPdfTextFragments(fragments, bytes);

        var latin = Encoding.Latin1.GetString(bytes);
        var streamCount = 0;
        foreach (Match match in Regex.Matches(latin, @"(?s)(?<dict><<.{0,1400}?>>)\s*stream\r?\n(?<data>.*?)\r?\nendstream", RegexOptions.None, TimeSpan.FromSeconds(2)))
        {
            if (++streamCount > MaxKlicContactPdfStreams) break;
            var dictionary = match.Groups["dict"].Value;
            var data = Encoding.Latin1.GetBytes(match.Groups["data"].Value);
            if (data.Length > MaxKlicContactPdfStreamBytes) continue;
            if (dictionary.Contains("FlateDecode", StringComparison.OrdinalIgnoreCase))
            {
                var inflated = TryInflatePdfStream(data);
                if (inflated.Length > 0)
                {
                    AddPdfTextFragments(fragments, inflated);
                }
            }
            else
            {
                AddPdfTextFragments(fragments, data);
            }
        }

        return string.Join("\n", fragments.Where(fragment => !string.IsNullOrWhiteSpace(fragment)));
    }

    private static byte[] TryInflatePdfStream(byte[] data)
    {
        foreach (var skip in new[] { 0, 1, 2 })
        {
            if (data.Length <= skip) continue;
            try
            {
                using var input = new MemoryStream(data, skip, data.Length - skip);
                using var output = new MemoryStream();
                using (var zlib = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true))
                {
                    CopyStreamLimited(zlib, output, MaxKlicContactPdfInflatedBytes);
                }

                return output.ToArray();
            }
            catch (System.Exception swallowedException)
            {
                // Try the next offset; some PDF writers include line break bytes around streams.
                AppLog.Swallowed(swallowedException);
            }
        }

        try
        {
            using var input = new MemoryStream(data);
            using var output = new MemoryStream();
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            {
                CopyStreamLimited(deflate, output, MaxKlicContactPdfInflatedBytes);
            }

            return output.ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static void AddPdfTextFragments(List<string> fragments, byte[] bytes)
    {
        foreach (var fragment in ExtractPdfTextFragments(bytes))
        {
            fragments.Add(fragment);
            if (fragments.Count >= 2500) break;
        }
    }

    private static IEnumerable<string> ExtractPdfTextFragments(byte[] bytes)
    {
        var text = Encoding.Latin1.GetString(bytes);
        foreach (Match match in Regex.Matches(text, @"\((?:\\.|[^\\)])*\)"))
        {
            var value = DecodePdfLiteralString(match.Value[1..^1]);
            if (!string.IsNullOrWhiteSpace(value)) yield return value;
        }

        foreach (Match match in Regex.Matches(text, @"<(?<hex>(?:[0-9A-Fa-f]\s*){4,})>"))
        {
            var value = DecodePdfHexString(match.Groups["hex"].Value);
            if (!string.IsNullOrWhiteSpace(value)) yield return value;
        }

        foreach (Match match in Regex.Matches(text, @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}|(?:\+31|0031|0)\s?\d[\d\s\-]{6,}\d|[A-Z]{2}\d{3,5}\s+[A-Za-z].{2,80}"))
        {
            var value = NormalizeContactCell(match.Value);
            if (!string.IsNullOrWhiteSpace(value)) yield return value;
        }
    }

    private static string DecodePdfLiteralString(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (ch != '\\' || index + 1 >= value.Length)
            {
                builder.Append(ch);
                continue;
            }

            var next = value[++index];
            builder.Append(next switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'b' => '\b',
                'f' => '\f',
                '(' => '(',
                ')' => ')',
                '\\' => '\\',
                _ when next is >= '0' and <= '7' => DecodePdfOctal(value, ref index, next),
                _ => next
            });
        }

        return NormalizeContactCell(builder.ToString());
    }

    private static char DecodePdfOctal(string value, ref int index, char first)
    {
        var octal = first.ToString();
        for (var count = 0; count < 2 && index + 1 < value.Length && value[index + 1] is >= '0' and <= '7'; count++)
        {
            octal += value[++index];
        }

        return (char)Convert.ToInt32(octal, 8);
    }

    private static string DecodePdfHexString(string hex)
    {
        var clean = Regex.Replace(hex, @"\s+", "");
        if (clean.Length % 2 == 1) clean += "0";
        var bytes = new byte[clean.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return NormalizeContactCell(Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2));
        }

        return NormalizeContactCell(Encoding.Latin1.GetString(bytes));
    }

    private static IReadOnlyList<KlicContactRow> ParseKlicContactRows(string text, string source)
    {
        var rows = new List<KlicContactRow>();
        var lines = text.Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeContactCell)
            .Where(line => line.Length > 0)
            .Where(line => !IsKlicContactNoiseLine(line))
            .ToList();

        string code = "";
        string netbeheerder = "";
        var pendingTheme = "";
        var pendingDetails = new List<string>();

        void FlushPending()
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(pendingTheme)) return;
            var details = string.Join(" ", pendingDetails);
            var email = FirstRegexValue(details, @"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}");
            var phones = Regex.Matches(details, @"(?:\+31|0031|0)\s?\d[\d\s\-]{6,}\d", RegexOptions.IgnoreCase)
                .Select(match => NormalizeContactCell(match.Value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var contact = NormalizeContactCell(Regex.Replace(details, @"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}|(?:\+31|0031|0)\s?\d[\d\s\-]{6,}\d|https?://\S+", "", RegexOptions.IgnoreCase));
            if (string.IsNullOrWhiteSpace(contact)) contact = "-";
            rows.Add(new KlicContactRow(
                code,
                netbeheerder,
                pendingTheme,
                contact,
                phones.Count > 0 ? phones[0] : "-",
                string.IsNullOrWhiteSpace(email) ? "-" : email,
                phones.Count > 1 ? phones[^1] : phones.Count > 0 ? phones[0] : "-",
                source));
            pendingTheme = "";
            pendingDetails.Clear();
        }

        foreach (var line in lines)
        {
            var manager = Regex.Match(line, @"^(?<code>[A-Z]{2}\d{3,5})\s+(?<name>.+)$");
            if (manager.Success)
            {
                FlushPending();
                code = manager.Groups["code"].Value;
                netbeheerder = CleanKlicManagerName(manager.Groups["name"].Value);
                if (line.Contains("Niet betrokken", StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add(new KlicContactRow(code, netbeheerder, "-", "Niet betrokken", "-", "-", "-", source));
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(code)) continue;

            var theme = ExtractKlicContactTheme(line);
            if (!string.IsNullOrWhiteSpace(theme))
            {
                FlushPending();
                pendingTheme = theme;
                var rest = NormalizeContactCell(line[theme.Length..]);
                if (!string.IsNullOrWhiteSpace(rest)) pendingDetails.Add(rest);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(pendingTheme))
            {
                pendingDetails.Add(line);
            }
        }

        FlushPending();
        return rows;
    }

    private static string CleanKlicManagerName(string value)
    {
        value = Regex.Replace(value, @"https?://\S+.*$", "", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bNiet betrokken\b.*$", "", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bthema\b.*$", "", RegexOptions.IgnoreCase);
        return NormalizeContactCell(value);
    }

    private static bool IsKlicContactNoiseLine(string line)
    {
        var lower = line.ToLowerInvariant();
        return lower is "thema" or "contact netinformatie" or "schade/storing" ||
               lower.Contains("netbeheerders met belangen", StringComparison.Ordinal) ||
               lower.Contains("onderstaande netbeheerders", StringComparison.Ordinal) ||
               lower.Contains("in onderstaande tabel", StringComparison.Ordinal) ||
               lower.Contains("klic-melding", StringComparison.Ordinal);
    }

    private static string ExtractKlicContactTheme(string line)
    {
        var themes = new[]
        {
            "middenspanning",
            "laagspanning",
            "gas lage druk",
            "gas hoge druk",
            "riool onder over- of onderdruk",
            "riool vrijverval",
            "water",
            "datatransport",
            "overig"
        };

        return themes.FirstOrDefault(theme => line.StartsWith(theme, StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    private static string FirstRegexValue(string value, string pattern)
    {
        var match = Regex.Match(value, pattern, RegexOptions.IgnoreCase);
        return match.Success ? NormalizeContactCell(match.Value) : "";
    }

    private static string NormalizeContactCell(string value) =>
        Regex.Replace(value.Replace('\u00a0', ' '), @"\s+", " ").Trim();

    private static string? ExtractDocumentForPreview(ProjectDocumentEntry doc) =>
        GisDocuments.ExtractForPreview(doc);

    private void TryOpenDocument(ProjectDocumentEntry doc)
    {
        try
        {
            var path = ExtractDocumentForPreview(doc);
            if (path is null) return;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            OutputText.Text += $"\n\nOpenen in Windows-viewer lukte niet: {exception.Message}";
        }
    }

    private static List<GeoJsonFeature> ReadProjectFeatures(ProjectFileRecord file)
    {
        var extension = System.IO.Path.GetExtension(file.LocalPath).ToLowerInvariant();
        try
        {
            return extension switch
            {
                ".zip" => ReadZipFeatures(file),
                ".geojson" or ".json" => ReadGeoJsonFeatures(System.IO.File.ReadAllText(file.LocalPath, Encoding.UTF8), file.FileType, file.DisplayName),
                ".gml" or ".xml" => ReadGmlFeatures(System.IO.File.ReadAllText(file.LocalPath, Encoding.UTF8), file.FileType, file.DisplayName),
                ".dxf" => ReadDxfFeatures(System.IO.File.ReadAllText(file.LocalPath, Encoding.UTF8), file.FileType, file.DisplayName),
                _ => []
            };
        }
        catch
        {
            return [];
        }
    }

    private static List<GeoJsonFeature> ReadZipFeatures(ProjectFileRecord file)
    {
        var features = new List<GeoJsonFeature>();
        using var archive = ZipFile.OpenRead(file.LocalPath);
        foreach (var entry in archive.Entries.Where(entry =>
                     entry.FullName.EndsWith(".gml", StringComparison.OrdinalIgnoreCase) ||
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                     entry.FullName.EndsWith(".geojson", StringComparison.OrdinalIgnoreCase) ||
                     entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                     entry.FullName.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = entry.Open();
            using var reader = new System.IO.StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();
            features.AddRange(entry.FullName.EndsWith(".geojson", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? ReadGeoJsonFeatures(text, file.FileType, entry.FullName)
                : entry.FullName.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)
                    ? ReadDxfFeatures(text, file.FileType, entry.FullName)
                    : ReadGmlFeatures(text, file.FileType, entry.FullName));
        }

        return features;
    }

    private static List<GeoJsonFeature> ReadGeoJsonFeatures(string text, string fileType, string sourceName)
    {
        return GisFeatureParser.ReadGeoJsonFeatures(text, fileType, sourceName);
    }

    private static GeoJsonGeometry? ConvertGeoJsonGeometry(JsonElement geometry)
    {
        return GisFeatureParser.ConvertGeoJsonGeometry(geometry);
    }

    private static List<GeoJsonFeature> ReadGmlFeatures(string text, string fileType, string sourceName)
    {
        if (fileType.Equals("BGT", StringComparison.OrdinalIgnoreCase))
        {
            var bgtFeatures = ReadBgtFeatures(text, sourceName);
            if (bgtFeatures.Count > 0) return bgtFeatures;
        }

        if (IsBagOrKadasterSource(fileType, sourceName))
        {
            var kadasterFeatures = ReadKadasterFeatures(text, fileType, sourceName);
            if (kadasterFeatures.Count > 0) return kadasterFeatures;
        }

        var imklFeatures = ReadImklFeatures(text, fileType, sourceName);
        if (imklFeatures.Count > 0) return imklFeatures;

        return GisFeatureParser.ReadGenericGmlFeatures(text, fileType);
    }

    private static bool IsBagOrKadasterSource(string fileType, string sourceName) =>
        GisBgtKadasterParser.IsBagOrKadasterSource(fileType, sourceName);

    private static List<GeoJsonFeature> ReadKadasterFeatures(string text, string fileType, string sourceName)
    {
        return GisBgtKadasterParser.ReadKadasterFeatures(text, fileType, sourceName);
    }

    private static List<GeoJsonFeature> ReadBgtFeatures(string text, string sourceName)
    {
        return GisBgtKadasterParser.ReadBgtFeatures(text, sourceName);
    }

    private static List<GeoJsonFeature> ReadDxfFeatures(string text, string fileType, string sourceName)
    {
        return GisDxfParser.ReadDxfFeatures(text, fileType, sourceName, LayerColor(fileType));
    }

    private static bool TryParseDxfDouble(string text, out double value) =>
        GisDxfParser.TryParseDxfDouble(text, out value);

    private static List<GeoJsonFeature> ReadImklFeatures(string text, string fileType, string sourceName)
    {
        return GisImklParser.ReadImklFeatures(text, fileType, sourceName);
    }

    private static bool IsRdText(string text) =>
        GisCoordinates.IsRdText(text);

    private static int GetSrsDimension(string tag)
    {
        return GisCoordinates.GetSrsDimension(tag);
    }

    private static List<double[]> ParseCoordinateText(string coordinateText, bool rd, int dimension)
    {
        return GisCoordinates.ParseCoordinateText(coordinateText, rd, dimension);
    }

    private static object ConvertPosition(JsonElement element)
    {
        return GisCoordinates.ConvertPosition(element);
    }

    private static List<object> ConvertPositionArray(JsonElement element)
    {
        return GisCoordinates.ConvertPositionArray(element);
    }

    private static List<object> ConvertNestedPositionArray(JsonElement element)
    {
        return GisCoordinates.ConvertNestedPositionArray(element);
    }

    private static bool LooksLikeRd(double x, double y) => GisCoordinates.LooksLikeRd(x, y);

    private static double[] RdToWgs84(double x, double y)
    {
        return GisCoordinates.RdToWgs84(x, y);
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static string TruncateText(string text, int maxLength)
    {
        text = Regex.Replace(text.Trim(), @"\s+", " ");
        if (maxLength <= 3 || text.Length <= maxLength) return text;
        return string.Concat(text.AsSpan(0, maxLength - 3), "...");
    }

    private static string LayerColor(string fileType) => fileType.ToUpperInvariant() switch
    {
        "LS" => "#7B00AA",
        "MS" => "#00BFEA",
        "GAS" => "#F4D000",
        "WATER" => "#001E8A",
        "DATA" => "#00B800",
        "KLIC" => "#888888",
        "BAG" or "KADASTER" => "#0057D8",
        "BGT" => "#CBD5E1",
        _ => "#101827"
    };

    private static string NormalizeKlicTheme(string? hrefOrTheme)
    {
        if (string.IsNullOrWhiteSpace(hrefOrTheme)) return "overig";
        var theme = hrefOrTheme.Split('/', '#').LastOrDefault(part => !string.IsNullOrWhiteSpace(part)) ?? hrefOrTheme;
        return theme.Trim();
    }

    private static string KlicThemeColor(string theme) => theme switch
    {
        "laagspanning" => "#7B00AA",
        "middenspanning" => "#00CCFF",
        "hoogspanning" => "#FF4400",
        "gasLageDruk" => "#FFFF00",
        "gasHogeDruk" => "#FF0000",
        "water" => "#0000CC",
        "datatransport" => "#00CC00",
        "rioolVrijverval" => "#AA00CC",
        "rioolOnderOverOfOnderdruk" => "#AA00CC",
        "warmte" => "#FF6600",
        "kadaster" or "kadaster perceel" or "kadaster grens" or "kadaster pand" or "kadaster label" => "#0057D8",
        _ => "#888888"
    };

    private static string KlicThemeLabel(string theme) => theme switch
    {
        "laagspanning" => "KLIC laagspanning",
        "middenspanning" => "KLIC middenspanning",
        "hoogspanning" => "KLIC hoogspanning",
        "gasLageDruk" => "KLIC gas lage druk",
        "gasHogeDruk" => "KLIC gas hoge druk",
        "water" => "KLIC water",
        "datatransport" => "KLIC data/telecom",
        "rioolVrijverval" => "KLIC riool vrijverval",
        "rioolOnderOverOfOnderdruk" => "KLIC riool druk",
        "warmte" => "KLIC warmte",
        "overig" => "KLIC overig",
        _ => $"KLIC {theme}"
    };

}
