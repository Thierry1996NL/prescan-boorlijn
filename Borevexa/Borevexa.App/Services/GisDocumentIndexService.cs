using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Borevexa.App.Models;
using Borevexa.Core.Services;

namespace Borevexa.App.Services;

public sealed class GisDocumentIndexService
{
    public IEnumerable<ProjectDocumentEntry> BuildEntries(IEnumerable<ProjectFileRecord> files)
    {
        foreach (var file in files.Where(file => File.Exists(file.LocalPath)))
        {
            var extension = Path.GetExtension(file.LocalPath).ToLowerInvariant();
            if (extension == ".zip")
            {
                foreach (var entry in ReadZipDocumentEntries(file))
                {
                    yield return entry;
                }
            }
            else if (IsDocumentExtension(extension))
            {
                yield return new ProjectDocumentEntry(
                    file.DisplayName,
                    extension.TrimStart('.').ToUpperInvariant(),
                    Math.Max(1, file.SizeBytes / 1024),
                    file.LocalPath,
                    null);
            }
        }
    }

    public IEnumerable<object> BuildIndex(IEnumerable<ProjectDocumentEntry> docs)
    {
        return docs.Select(doc => new
        {
            doc.Name,
            doc.Type,
            doc.SizeKb,
            doc.LocalPath,
            doc.ZipEntryName,
            readable = IsTextDocumentType(doc.Type),
            content = IsTextDocumentType(doc.Type) ? ReadDocumentContent(doc, 24000) : null
        });
    }

    public string ReadDocumentContent(ProjectDocumentEntry doc, int maxChars)
    {
        try
        {
            if (!IsTextDocumentType(doc.Type))
            {
                return $"{doc.Type}-bestand. Klik opent het bestand in de standaard Windows-viewer.\nBron: {doc.LocalPath}";
            }

            using var stream = OpenDocumentStream(doc);
            if (stream is null) return "Bestand kon niet worden geopend.";
            return ReadTextStream(stream, maxChars);
        }
        catch (Exception exception)
        {
            return $"Inhoud kon niet worden gelezen: {exception.Message}";
        }
    }

    public bool IsTextDocumentType(string type) =>
        type.Equals("XML", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("GML", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("HTML", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("HTM", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("JSON", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("TXT", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("CSV", StringComparison.OrdinalIgnoreCase);

    public Stream? OpenDocumentStream(ProjectDocumentEntry doc)
    {
        if (doc.ZipEntryName is null) return File.OpenRead(doc.LocalPath);

        var archive = ZipFile.OpenRead(doc.LocalPath);
        var entry = archive.GetEntry(doc.ZipEntryName);
        if (entry is null)
        {
            archive.Dispose();
            return null;
        }

        return new ZipEntryReadStream(archive, entry.Open());
    }

    public string? ExtractForPreview(ProjectDocumentEntry doc)
    {
        if (doc.ZipEntryName is null) return File.Exists(doc.LocalPath) ? doc.LocalPath : null;

        using var archive = ZipFile.OpenRead(doc.LocalPath);
        var entry = archive.GetEntry(doc.ZipEntryName);
        if (entry is null) return null;

        var previewDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Borevexa",
            "DocumentPreview");
        Directory.CreateDirectory(previewDir);

        var safeName = Regex.Replace(Path.GetFileName(doc.Name), @"[^\w\-. ]+", "_");
        var extension = Path.GetExtension(safeName);
        var baseName = Path.GetFileNameWithoutExtension(safeName);
        var target = Path.Combine(previewDir, $"{baseName}-{doc.Id}{extension}");
        if (TryExtractZipEntryToFile(entry, target, overwrite: true))
        {
            return target;
        }

        var uniqueTarget = Path.Combine(previewDir, $"{baseName}-{doc.Id}-{DateTime.Now:yyyyMMddHHmmssfff}{extension}");
        return TryExtractZipEntryToFile(entry, uniqueTarget, overwrite: false) ? uniqueTarget : null;
    }

    public static bool IsDocumentExtension(string extension) =>
        extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".gml", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".htm", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<ProjectDocumentEntry> ReadZipDocumentEntries(ProjectFileRecord file)
    {
        using var archive = ZipFile.OpenRead(file.LocalPath);
        foreach (var entry in archive.Entries
                     .Where(entry => IsDocumentExtension(Path.GetExtension(entry.FullName)))
                     .OrderBy(entry => entry.FullName))
        {
            yield return new ProjectDocumentEntry(
                Path.GetFileName(entry.FullName),
                Path.GetExtension(entry.FullName).TrimStart('.').ToUpperInvariant(),
                Math.Max(1, entry.Length / 1024),
                file.LocalPath,
                entry.FullName);
        }
    }

    private static string ReadTextStream(Stream stream, int maxChars)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var buffer = new char[maxChars];
        var read = reader.ReadBlock(buffer, 0, maxChars);
        var suffix = reader.Peek() >= 0 ? "\n\n... preview ingekort voor snelheid." : "";
        return new string(buffer, 0, read) + suffix;
    }

    private static bool TryExtractZipEntryToFile(ZipArchiveEntry entry, string target, bool overwrite)
    {
        try
        {
            entry.ExtractToFile(target, overwrite);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class ZipEntryReadStream(ZipArchive archive, Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                archive.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
