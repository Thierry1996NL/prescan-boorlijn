namespace Borevexa.Prescan.App.Reports.Renderers;

internal static class ReportText
{
    public static string ShortCell(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "...";
    }

    public static string FormatBytes(double bytes)
    {
        if (!double.IsFinite(bytes) || bytes <= 0) return "-";
        if (bytes >= 1024 * 1024) return $"{bytes / 1024d / 1024d:N1} MB";
        return $"{bytes / 1024d:N0} KB";
    }

    public static bool IsDesignImportType(string fileType) =>
        fileType.Contains("LS", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("MS", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("Gas", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("Water", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("Data", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("custom", StringComparison.OrdinalIgnoreCase);

    public static bool IsEnvironmentImportType(string fileType) =>
        fileType.Contains("BAG", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("Kadaster", StringComparison.OrdinalIgnoreCase) ||
        fileType.Contains("BGT", StringComparison.OrdinalIgnoreCase);

    public static int ImportedFileTypeOrder(string fileType)
    {
        var normalized = NormalizeImportedFileType(fileType);
        return normalized switch
        {
            "KLIC" => 0,
            "BAG/Kadaster" => 1,
            "BGT" => 2,
            "Laagspanning" => 3,
            "Middenspanning" => 4,
            _ => 9
        };
    }

    public static string NormalizeImportedFileType(string fileType)
    {
        if (fileType.Contains("KLIC", StringComparison.OrdinalIgnoreCase)) return "KLIC";
        if (fileType.Contains("BAG", StringComparison.OrdinalIgnoreCase) || fileType.Contains("Kadaster", StringComparison.OrdinalIgnoreCase)) return "BAG/Kadaster";
        if (fileType.Contains("BGT", StringComparison.OrdinalIgnoreCase)) return "BGT";
        if (fileType.Contains("LS", StringComparison.OrdinalIgnoreCase)) return "Laagspanning";
        if (fileType.Contains("MS", StringComparison.OrdinalIgnoreCase)) return "Middenspanning";
        return string.IsNullOrWhiteSpace(fileType) ? "-" : fileType;
    }

    public static string DescribeImportedFileType(string fileType)
    {
        if (fileType.Contains("KLIC", StringComparison.OrdinalIgnoreCase)) return "Kabels en leidingen";
        if (fileType.Contains("BAG", StringComparison.OrdinalIgnoreCase) || fileType.Contains("Kadaster", StringComparison.OrdinalIgnoreCase)) return "Percelen, adressen en gebouwen";
        if (fileType.Contains("BGT", StringComparison.OrdinalIgnoreCase)) return "BGT-ondergrond en oppervlakken";
        if (IsDesignImportType(fileType)) return "Ontwerp-/netinformatie";
        return "Projectbron";
    }
}
