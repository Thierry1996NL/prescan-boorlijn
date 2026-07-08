namespace Borevexa.Prescan.App;

internal sealed record TracePointRow(int Index, string Role, double X, double Y);

internal sealed record RdPoint(double X, double Y);

internal sealed record TraceProjection(double Distance, double Offset);

internal sealed record CadastralParcelPolygon(
    IReadOnlyList<RdPoint> Ring,
    IReadOnlyList<IReadOnlyList<RdPoint>> Holes,
    string CadastralMunicipality,
    string Section,
    string ParcelNumber,
    string CadastralObjectId,
    double Area);

internal sealed record BgtHolderPolygon(
    IReadOnlyList<RdPoint> Ring,
    IReadOnlyList<IReadOnlyList<RdPoint>> Holes,
    string BronhouderCode,
    double Area);

internal sealed record ParcelOwnerAnalysis(
    IReadOnlyList<TracePointRow> TraceRows,
    double TraceLength,
    IReadOnlyList<ParcelOwnerSegment> Segments,
    IReadOnlyList<CadastralParcelPolygon> ParcelPolygons,
    IReadOnlyList<BgtHolderPolygon> HolderPolygons);

internal sealed record ParcelOwnerSegment(
    double Start,
    double End,
    string CadastralMunicipality,
    string Section,
    string ParcelNumber,
    string CadastralObjectId,
    string BgtHolderCode,
    string BgtHolderCategory,
    string BgtHolderName,
    string ZroStatus,
    IReadOnlyList<RdPoint> TracePath,
    CadastralParcelPolygon? Parcel)
{
    public double Length => Math.Max(0, End - Start);
}
