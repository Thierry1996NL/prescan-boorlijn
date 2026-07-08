# Borevexa

Native Windows (WPF/.NET 8) applicatie voor de Borevexa prescan-rapportages.

## Projectstructuur

- `Borevexa.App` - WPF desktop UI
- `Borevexa.Core` - projectmodellen, stappen en workflow
- `Borevexa.Geo` - voorbereiding voor BGT, BRO, AHN, RD/WGS84 en later Mapsui/GDAL
- `Borevexa.Cad` - voorbereiding voor DXF/DWG en AutoCAD-koppeling

## Lokale opslag

De app gebruikt geen online database. Projecten, stapdata, chatberichten en gekoppelde bestanden worden lokaal opgeslagen.

- Database: `%LOCALAPPDATA%\Borevexa\borevexa-prescan.sqlite`
- Projectbestanden: `%LOCALAPPDATA%\Borevexa\ProjectFiles`

De database is SQLite. Voor GIS-geometrie is de volgende stap een GeoPackage-bestand naast de SQLite database, zodat QGIS/GDAL/OGR en AutoCAD-export later logisch kunnen aansluiten.

## Starten

```powershell
cd "C:\Users\ThierryPapenhuijzen\OneDrive - Inpark\Documenten\GitHub\Borevexa_V2.0\Borevexa"
dotnet run --project .\Borevexa.App\Borevexa.App.csproj
```

## Volgende technische stappen

1. Projectdialoog en formulieropslag voor stap 1 toevoegen.
2. Mapsui toevoegen als native kaartviewer.
3. GDAL/OGR toevoegen voor GML, GeoJSON, DXF en GeoPackage.
4. NetTopologySuite toevoegen voor kruisingen, buffers en analyses.
5. DXF-export concreet maken.
6. AutoCAD .NET plugin ontwerpen voor directe import, lagen en labels.
