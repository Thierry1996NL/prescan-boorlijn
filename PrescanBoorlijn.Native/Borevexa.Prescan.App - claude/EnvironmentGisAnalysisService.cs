using System.Text.RegularExpressions;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using NtsGeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using NtsLineString = NetTopologySuite.Geometries.LineString;
using NtsLinearRing = NetTopologySuite.Geometries.LinearRing;
using NtsPolygon = NetTopologySuite.Geometries.Polygon;

namespace Borevexa.Prescan.App;

internal sealed class EnvironmentGisAnalysisService
{
    public IReadOnlyList<ParcelOwnerSegment> AnalyzeParcelOwnerSegments(
        IReadOnlyList<TracePointRow> traceRows,
        double profileTotal,
        IReadOnlyList<CadastralParcelPolygon> parcelPolygons,
        IReadOnlyList<BgtHolderPolygon> holderPolygons)
    {
        if (traceRows.Count < 2 || profileTotal <= 0 || parcelPolygons.Count == 0) return [];

        var traceDistances = BuildTraceDistances(traceRows);
        if (traceDistances.Count < 2 || traceDistances[^1] <= 0) return [];

        var overlaySegments = BuildSegmentsByGeometryOverlay(
            traceRows,
            traceDistances,
            parcelPolygons,
            holderPolygons,
            profileTotal);
        if (overlaySegments.Count > 0)
        {
            return overlaySegments;
        }

        return BuildSegmentsByParcelTransitions(
            traceRows,
            traceDistances,
            parcelPolygons,
            holderPolygons,
            profileTotal);
    }

    private static IReadOnlyList<ParcelOwnerSegment> BuildSegmentsByGeometryOverlay(
        IReadOnlyList<TracePointRow> traceRows,
        IReadOnlyList<double> traceDistances,
        IReadOnlyList<CadastralParcelPolygon> parcelPolygons,
        IReadOnlyList<BgtHolderPolygon> holderPolygons,
        double profileTotal)
    {
        var traceTotal = traceDistances[^1];
        var factory = new NtsGeometryFactory();
        var traceLine = factory.CreateLineString(traceRows.Select(row => new NtsCoordinate(row.X, row.Y)).ToArray());
        var rawSegments = new List<ParcelOwnerSegment>();

        foreach (var parcel in parcelPolygons)
        {
            var polygon = CreateNtsPolygon(factory, parcel);
            if (polygon is null || polygon.IsEmpty) continue;

            NtsGeometry intersection;
            try
            {
                intersection = polygon.Intersection(traceLine);
            }
            catch
            {
                try
                {
                    intersection = polygon.Buffer(0).Intersection(traceLine);
                }
                catch
                {
                    continue;
                }
            }

            foreach (var line in EnumerateNtsLineStrings(intersection))
            {
                if (line.Length < 0.05 || line.Coordinates.Length < 2) continue;

                var first = line.Coordinates[0];
                var last = line.Coordinates[^1];
                var startProjection = ProjectPointOnTrace(first.X, first.Y, traceRows, traceDistances);
                var endProjection = ProjectPointOnTrace(last.X, last.Y, traceRows, traceDistances);
                var actualStart = Math.Min(startProjection.Distance, endProjection.Distance);
                var actualEnd = Math.Max(startProjection.Distance, endProjection.Distance);
                if (actualEnd - actualStart < 0.2) continue;

                var midpoint = InterpolateTracePoint(traceRows, traceDistances, (actualStart + actualEnd) / 2);
                var holder = holderPolygons
                    .Where(candidate => PolygonContains(candidate, midpoint.X, midpoint.Y))
                    .OrderBy(candidate => candidate.Area)
                    .FirstOrDefault();

                AddParcelSegment(
                    rawSegments,
                    profileTotal * actualStart / traceTotal,
                    profileTotal * actualEnd / traceTotal,
                    parcel,
                    holder?.BronhouderCode ?? "",
                    BuildTracePath(traceRows, traceDistances, actualStart, actualEnd));
            }
        }

        return MergeParcelOwnerSegments(rawSegments);
    }

    private static IReadOnlyList<ParcelOwnerSegment> BuildSegmentsByParcelTransitions(
        IReadOnlyList<TracePointRow> traceRows,
        IReadOnlyList<double> traceDistances,
        IReadOnlyList<CadastralParcelPolygon> parcelPolygons,
        IReadOnlyList<BgtHolderPolygon> holderPolygons,
        double profileTotal)
    {
        if (traceRows.Count < 2 || traceDistances.Count < 2 || traceDistances[^1] <= 0 || profileTotal <= 0) return [];

        var breakpoints = new List<double> { 0, traceDistances[^1] };
        breakpoints.AddRange(traceDistances);
        AddParcelTransitionBreakpoints(breakpoints, traceRows, traceDistances, parcelPolygons);
        breakpoints = NormalizeBreakpoints(breakpoints, traceDistances[^1]);
        if (breakpoints.Count < 2) return [];

        var segments = new List<ParcelOwnerSegment>();
        var traceTotal = traceDistances[^1];
        for (var i = 1; i < breakpoints.Count; i++)
        {
            var startTrace = breakpoints[i - 1];
            var endTrace = breakpoints[i];
            if (endTrace - startTrace < 0.2) continue;

            var midpoint = InterpolateTracePoint(traceRows, traceDistances, (startTrace + endTrace) / 2);
            var parcel = FindParcelAtDistance(traceRows, traceDistances, (startTrace + endTrace) / 2, parcelPolygons);
            if (parcel is null) continue;

            var holder = holderPolygons
                .Where(candidate => PolygonContains(candidate, midpoint.X, midpoint.Y))
                .OrderBy(candidate => candidate.Area)
                .FirstOrDefault();

            AddParcelSegment(
                segments,
                profileTotal * startTrace / traceTotal,
                profileTotal * endTrace / traceTotal,
                parcel,
                holder?.BronhouderCode ?? "",
                BuildTracePath(traceRows, traceDistances, startTrace, endTrace));
        }

        return MergeParcelOwnerSegments(segments);
    }

    private static IReadOnlyList<ParcelOwnerSegment> MergeParcelOwnerSegments(IEnumerable<ParcelOwnerSegment> source)
    {
        var ordered = source
            .Where(segment => segment.Length >= 0.2)
            .OrderBy(segment => segment.Start)
            .ThenBy(segment => segment.End)
            .ToList();
        if (ordered.Count <= 1) return ordered;

        var result = new List<ParcelOwnerSegment>();
        var current = ordered[0];
        for (var i = 1; i < ordered.Count; i++)
        {
            var next = ordered[i];
            if (SameParcel(current, next) && next.Start <= current.End + 0.15)
            {
                current = current with
                {
                    End = Math.Max(current.End, next.End),
                    TracePath = MergeTracePaths(current.TracePath, next.TracePath),
                    Parcel = current.Parcel ?? next.Parcel
                };
                continue;
            }

            result.Add(current);
            current = next;
        }

        result.Add(current);
        return result;
    }

    private static IReadOnlyList<RdPoint> MergeTracePaths(IReadOnlyList<RdPoint> first, IReadOnlyList<RdPoint> second)
    {
        if (first.Count == 0) return second;
        if (second.Count == 0) return first;

        var result = first.ToList();
        var startIndex = PointsAlmostEqual(result[^1], second[0]) ? 1 : 0;
        for (var i = startIndex; i < second.Count; i++)
        {
            result.Add(second[i]);
        }

        return result;
    }

    private static bool SameParcel(ParcelOwnerSegment a, ParcelOwnerSegment b) =>
        NormalizeFeatureKey(a.CadastralObjectId).Equals(NormalizeFeatureKey(b.CadastralObjectId), StringComparison.OrdinalIgnoreCase) &&
        a.CadastralMunicipality.Equals(b.CadastralMunicipality, StringComparison.OrdinalIgnoreCase) &&
        a.Section.Equals(b.Section, StringComparison.OrdinalIgnoreCase) &&
        a.ParcelNumber.Equals(b.ParcelNumber, StringComparison.OrdinalIgnoreCase) &&
        a.BgtHolderCode.Equals(b.BgtHolderCode, StringComparison.OrdinalIgnoreCase);

    private static NtsPolygon? CreateNtsPolygon(NtsGeometryFactory factory, CadastralParcelPolygon parcel)
    {
        var shell = CreateNtsRing(factory, parcel.Ring);
        if (shell is null) return null;

        var holes = parcel.Holes
            .Select(hole => CreateNtsRing(factory, hole))
            .Where(ring => ring is not null)
            .Cast<NtsLinearRing>()
            .ToArray();
        return factory.CreatePolygon(shell, holes);
    }

    private static NtsLinearRing? CreateNtsRing(NtsGeometryFactory factory, IReadOnlyList<RdPoint> points)
    {
        if (points.Count < 4) return null;

        var coordinates = points.Select(point => new NtsCoordinate(point.X, point.Y)).ToList();
        if (!coordinates[0].Equals2D(coordinates[^1]))
        {
            coordinates.Add(new NtsCoordinate(coordinates[0]));
        }

        return coordinates.Count >= 4 ? factory.CreateLinearRing(coordinates.ToArray()) : null;
    }

    private static IEnumerable<NtsLineString> EnumerateNtsLineStrings(NtsGeometry geometry)
    {
        if (geometry.IsEmpty) yield break;
        if (geometry is NtsLineString line)
        {
            yield return line;
            yield break;
        }

        for (var i = 0; i < geometry.NumGeometries; i++)
        {
            foreach (var child in EnumerateNtsLineStrings(geometry.GetGeometryN(i)))
            {
                yield return child;
            }
        }
    }

    private static void AddParcelTransitionBreakpoints(
        List<double> breakpoints,
        IReadOnlyList<TracePointRow> traceRows,
        IReadOnlyList<double> traceDistances,
        IReadOnlyList<CadastralParcelPolygon> parcelPolygons)
    {
        var total = traceDistances[^1];
        if (total <= 0 || parcelPolygons.Count == 0) return;

        var step = Math.Max(0.15, Math.Min(0.75, total / 250));
        var previousDistance = 0d;
        var previousParcel = FindParcelAtDistance(traceRows, traceDistances, previousDistance, parcelPolygons);
        var previousKey = ParcelPolygonKey(previousParcel);

        for (var distance = step; distance <= total + 0.001; distance += step)
        {
            var currentDistance = Math.Min(total, distance);
            var currentParcel = FindParcelAtDistance(traceRows, traceDistances, currentDistance, parcelPolygons);
            var currentKey = ParcelPolygonKey(currentParcel);
            if (!currentKey.Equals(previousKey, StringComparison.OrdinalIgnoreCase))
            {
                breakpoints.Add(FindParcelTransitionDistance(
                    traceRows,
                    traceDistances,
                    parcelPolygons,
                    previousDistance,
                    currentDistance,
                    previousKey));
            }

            previousDistance = currentDistance;
            previousKey = currentKey;
        }
    }

    private static double FindParcelTransitionDistance(
        IReadOnlyList<TracePointRow> traceRows,
        IReadOnlyList<double> traceDistances,
        IReadOnlyList<CadastralParcelPolygon> parcelPolygons,
        double low,
        double high,
        string lowKey)
    {
        for (var i = 0; i < 24; i++)
        {
            var mid = (low + high) / 2;
            var midKey = ParcelPolygonKey(FindParcelAtDistance(traceRows, traceDistances, mid, parcelPolygons));
            if (midKey.Equals(lowKey, StringComparison.OrdinalIgnoreCase))
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return (low + high) / 2;
    }

    private static CadastralParcelPolygon? FindParcelAtDistance(
        IReadOnlyList<TracePointRow> traceRows,
        IReadOnlyList<double> traceDistances,
        double distance,
        IReadOnlyList<CadastralParcelPolygon> parcelPolygons)
    {
        var point = InterpolateTracePoint(traceRows, traceDistances, Math.Clamp(distance, 0, traceDistances[^1]));
        return parcelPolygons
            .Where(candidate => PolygonContains(candidate, point.X, point.Y))
            .OrderBy(candidate => candidate.Area)
            .FirstOrDefault();
    }

    private static IReadOnlyList<RdPoint> BuildTracePath(
        IReadOnlyList<TracePointRow> traceRows,
        IReadOnlyList<double> traceDistances,
        double startDistance,
        double endDistance)
    {
        if (traceRows.Count < 2 || traceDistances.Count < 2 || traceDistances[^1] <= 0) return [];

        var start = Math.Clamp(Math.Min(startDistance, endDistance), 0, traceDistances[^1]);
        var end = Math.Clamp(Math.Max(startDistance, endDistance), 0, traceDistances[^1]);
        if (end <= start) return [];

        var result = new List<RdPoint>();
        var startPoint = InterpolateTracePoint(traceRows, traceDistances, start);
        result.Add(new RdPoint(startPoint.X, startPoint.Y));

        for (var i = 1; i < traceRows.Count - 1; i++)
        {
            if (traceDistances[i] > start && traceDistances[i] < end)
            {
                result.Add(new RdPoint(traceRows[i].X, traceRows[i].Y));
            }
        }

        var endPoint = InterpolateTracePoint(traceRows, traceDistances, end);
        result.Add(new RdPoint(endPoint.X, endPoint.Y));
        return result;
    }

    private static void AddParcelSegment(
        List<ParcelOwnerSegment> segments,
        double start,
        double end,
        CadastralParcelPolygon parcel,
        string bgtHolderCode,
        IReadOnlyList<RdPoint> tracePath)
    {
        if (end - start < 0.5) return;

        var code = NormalizeBronhouderCode(bgtHolderCode);
        segments.Add(new ParcelOwnerSegment(
            start,
            end,
            ValueOrDash(parcel.CadastralMunicipality),
            ValueOrDash(parcel.Section),
            ValueOrDash(parcel.ParcelNumber),
            ValueOrDash(parcel.CadastralObjectId),
            ValueOrDash(code),
            BronhouderCategory(code),
            BronhouderName(code),
            "Handmatig invullen",
            tracePath,
            parcel));
    }

    private static List<double> BuildTraceDistances(IReadOnlyList<TracePointRow> traceRows)
    {
        var distances = new List<double>();
        var total = 0d;
        for (var i = 0; i < traceRows.Count; i++)
        {
            if (i > 0)
            {
                var previous = traceRows[i - 1];
                var current = traceRows[i];
                total += Math.Sqrt(Math.Pow(current.X - previous.X, 2) + Math.Pow(current.Y - previous.Y, 2));
            }

            distances.Add(total);
        }

        return distances;
    }

    private static TracePointRow InterpolateTracePoint(IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<double> distances, double targetDistance)
    {
        if (traceRows.Count == 0) return new TracePointRow(1, "Dieptepunt", 0, 0);
        if (traceRows.Count == 1 || distances.Count != traceRows.Count) return traceRows[0];

        var clamped = Math.Clamp(targetDistance, 0, distances[^1]);
        for (var i = 1; i < traceRows.Count; i++)
        {
            if (distances[i] < clamped) continue;
            var previous = traceRows[i - 1];
            var next = traceRows[i];
            var span = Math.Max(0.001, distances[i] - distances[i - 1]);
            var ratio = (clamped - distances[i - 1]) / span;
            return new TracePointRow(0, "Dieptepunt", previous.X + (next.X - previous.X) * ratio, previous.Y + (next.Y - previous.Y) * ratio);
        }

        return traceRows[^1];
    }

    private static TraceProjection ProjectPointOnTrace(double x, double y, IReadOnlyList<TracePointRow> traceRows, IReadOnlyList<double> distances)
    {
        if (traceRows.Count == 0) return new TraceProjection(0, double.MaxValue);

        var bestDistance = 0d;
        var bestOffset = double.MaxValue;
        for (var i = 1; i < traceRows.Count; i++)
        {
            var a = traceRows[i - 1];
            var b = traceRows[i];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0) continue;

            var ratio = Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / lengthSquared, 0, 1);
            var px = a.X + dx * ratio;
            var py = a.Y + dy * ratio;
            var offset = Math.Sqrt(Math.Pow(x - px, 2) + Math.Pow(y - py, 2));
            if (offset >= bestOffset) continue;

            bestOffset = offset;
            bestDistance = distances[i - 1] + Math.Sqrt(lengthSquared) * ratio;
        }

        return new TraceProjection(bestDistance, bestOffset);
    }

    private static bool PolygonContains(CadastralParcelPolygon polygon, double x, double y)
    {
        if (!PointInPolygon(x, y, polygon.Ring)) return false;
        return !polygon.Holes.Any(hole => PointInPolygon(x, y, hole));
    }

    private static bool PolygonContains(BgtHolderPolygon polygon, double x, double y)
    {
        if (!PointInPolygon(x, y, polygon.Ring)) return false;
        return !polygon.Holes.Any(hole => PointInPolygon(x, y, hole));
    }

    private static bool PointInPolygon(double x, double y, IReadOnlyList<RdPoint> ring)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var pi = ring[i];
            var pj = ring[j];
            if (((pi.Y > y) != (pj.Y > y)) &&
                (x < (pj.X - pi.X) * (y - pi.Y) / ((pj.Y - pi.Y) == 0 ? 0.0000001 : (pj.Y - pi.Y)) + pi.X))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static List<double> NormalizeBreakpoints(IEnumerable<double> breakpoints, double maxDistance) =>
        breakpoints
            .Where(double.IsFinite)
            .Where(distance => distance >= -0.001 && distance <= maxDistance + 0.001)
            .Select(distance => Math.Clamp(distance, 0, maxDistance))
            .OrderBy(distance => distance)
            .Aggregate(new List<double>(), (result, distance) =>
            {
                if (result.Count == 0 || Math.Abs(distance - result[^1]) > 0.05)
                {
                    result.Add(distance);
                }

                return result;
            });

    private static string ParcelPolygonKey(CadastralParcelPolygon? parcel) =>
        parcel is null
            ? ""
            : string.Join("|", parcel.CadastralMunicipality, parcel.Section, parcel.ParcelNumber, parcel.CadastralObjectId);

    private static bool PointsAlmostEqual(RdPoint a, RdPoint b) =>
        Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01;

    private static string NormalizeFeatureKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return Regex.Replace(value.Trim(), @"\s+", "").ToUpperInvariant();
    }

    private static string NormalizeBronhouderCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var match = Regex.Match(code.Trim(), @"[GWPL]\d{4}", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : Regex.Replace(code.Trim(), @"\s+", " ");
    }

    private static string BronhouderCategory(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code == "-") return "-";
        return code[0] switch
        {
            'G' => "Gemeente",
            'W' => "Waterschap",
            'P' => "Provincie",
            'L' => "Rijk/landelijk",
            _ => "Onbekend"
        };
    }

    private static string BronhouderName(string code) => code switch
    {
        "G0074" => "Gemeente Heerenveen",
        "W0653" => "Wetterskip Fryslan",
        "P0021" => "Provincie Fryslan",
        "L0001" => "Economische Zaken / rijk",
        "" or "-" => "-",
        _ => "Onbekend"
    };

    private static string ValueOrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
