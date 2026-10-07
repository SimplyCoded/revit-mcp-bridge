using System;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace RevitMcpPlugin.Commands
{
    internal static class ElementCommands
    {
        // ── helpers ──────────────────────────────────────────────────────────────

        private static XYZ MmToFt(JsonElement pt) =>
            new XYZ(
                (pt.TryGetProperty("x", out var x) ? x.GetDouble() : 0) / 304.8,
                (pt.TryGetProperty("y", out var y) ? y.GetDouble() : 0) / 304.8,
                (pt.TryGetProperty("z", out var z) ? z.GetDouble() : 0) / 304.8
            );

        private static ElementId GetId(int v) => new ElementId((long)v);

        // ── create_level ─────────────────────────────────────────────────────────

        public static string CreateLevel(Document doc, JsonElement args)
        {
            if (!args.TryGetProperty("elevationMm", out var elProp))
                return "{\"error\":\"elevationMm is required\"}";

            double elevFt = elProp.GetDouble() / 304.8;
            string? name = args.TryGetProperty("name", out var nm) ? nm.GetString() : null;

            using var tx = new Transaction(doc, "MCP: Create Level");
            tx.Start();
            var level = Level.Create(doc, elevFt);
            if (!string.IsNullOrEmpty(name)) level.Name = name;
            tx.Commit();

            return JsonSerializer.Serialize(new
            {
                status      = "Success",
                id          = level.Id.Value,
                name        = level.Name,
                elevationMm = Math.Round(level.Elevation * 304.8, 1),
            });
        }

        // ── create_grid ──────────────────────────────────────────────────────────

        public static string CreateGrid(Document doc, JsonElement args)
        {
            if (!args.TryGetProperty("startPoint", out var spProp) ||
                !args.TryGetProperty("endPoint",   out var epProp))
                return "{\"error\":\"startPoint and endPoint are required\"}";

            XYZ start = MmToFt(spProp);
            XYZ end   = MmToFt(epProp);
            string? name = args.TryGetProperty("name", out var nm) ? nm.GetString() : null;

            using var tx = new Transaction(doc, "MCP: Create Grid");
            tx.Start();
            var line = Line.CreateBound(start, end);
            var grid = Grid.Create(doc, line);
            if (!string.IsNullOrEmpty(name)) grid.Name = name;
            tx.Commit();

            return JsonSerializer.Serialize(new
            {
                status = "Success",
                id     = grid.Id.Value,
                name   = grid.Name,
            });
        }

        // ── create_line_based_element ────────────────────────────────────────────

        public static string CreateLineBasedElement(Document doc, JsonElement args)
        {
            if (!args.TryGetProperty("familyTypeId", out var ftProp))
                return "{\"error\":\"familyTypeId is required\"}";
            if (!args.TryGetProperty("startPoint", out var spProp) ||
                !args.TryGetProperty("endPoint",   out var epProp))
                return "{\"error\":\"startPoint and endPoint are required\"}";

            var typeId = GetId(ftProp.GetInt32());
            XYZ start  = MmToFt(spProp);
            XYZ end    = MmToFt(epProp);

            var symbol = doc.GetElement(typeId) as FamilySymbol;
            if (symbol == null)
                return $"{{\"error\":\"Family type {ftProp.GetInt32()} not found\"}}";

            string category = symbol.Category?.Name ?? "";
            bool isWall = category.Contains("Wall", StringComparison.OrdinalIgnoreCase);

            using var tx = new Transaction(doc, "MCP: Create Line Element");
            tx.Start();
            if (!symbol.IsActive) symbol.Activate();

            Element created;
            if (isWall)
            {
                // Wall: needs height (default 3000mm = ~9.84 ft)
                double heightFt = args.TryGetProperty("heightMm", out var hm)
                    ? hm.GetDouble() / 304.8 : 9.84;

                Level? level = null;
                if (args.TryGetProperty("levelId", out var li))
                    level = doc.GetElement(GetId(li.GetInt32())) as Level;
                level ??= new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).FirstOrDefault();

                var wallType = typeId;
                var wall = Wall.Create(doc, Line.CreateBound(start, end),
                    wallType, level?.Id ?? ElementId.InvalidElementId, heightFt, 0, false, false);
                created = wall;
            }
            else
            {
                Level? level = null;
                if (args.TryGetProperty("levelId", out var li))
                    level = doc.GetElement(GetId(li.GetInt32())) as Level;
                level ??= new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).FirstOrDefault();

                var line = Line.CreateBound(start, end);
                var structType = category.Contains("Column", StringComparison.OrdinalIgnoreCase)
                    ? StructuralType.Column
                    : StructuralType.Beam;
                created = doc.Create.NewFamilyInstance(line, symbol, level, structType);
            }

            tx.Commit();

            return JsonSerializer.Serialize(new
            {
                status   = "Success",
                id       = created.Id.Value,
                category,
            });
        }

        // ── create_point_based_element ───────────────────────────────────────────

        public static string CreatePointBasedElement(Document doc, JsonElement args)
        {
            if (!args.TryGetProperty("familyTypeId", out var ftProp))
                return "{\"error\":\"familyTypeId is required\"}";
            if (!args.TryGetProperty("point", out var ptProp))
                return "{\"error\":\"point is required\"}";

            var symbol = doc.GetElement(GetId(ftProp.GetInt32())) as FamilySymbol;
            if (symbol == null)
                return $"{{\"error\":\"Family type {ftProp.GetInt32()} not found\"}}";

            XYZ point = MmToFt(ptProp);

            Level? level = null;
            if (args.TryGetProperty("levelId", out var li))
                level = doc.GetElement(GetId(li.GetInt32())) as Level;
            level ??= new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => Math.Abs(l.Elevation - point.Z)).FirstOrDefault();

            string category = symbol.Category?.Name ?? "";
            bool isStructural = category.Contains("Column", StringComparison.OrdinalIgnoreCase)
                || category.Contains("Framing", StringComparison.OrdinalIgnoreCase)
                || category.Contains("Foundation", StringComparison.OrdinalIgnoreCase);

            using var tx = new Transaction(doc, "MCP: Create Point Element");
            tx.Start();
            if (!symbol.IsActive) symbol.Activate();

            var instance = doc.Create.NewFamilyInstance(
                point, symbol, level,
                isStructural ? StructuralType.Column : StructuralType.NonStructural);

            if (args.TryGetProperty("rotation", out var rot))
            {
                double angle = rot.GetDouble() * Math.PI / 180.0;
                var axis = Line.CreateBound(point, point + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(doc, instance.Id, axis, angle);
            }

            tx.Commit();

            return JsonSerializer.Serialize(new
            {
                status   = "Success",
                id       = instance.Id.Value,
                category,
            });
        }

        // ── create_room ──────────────────────────────────────────────────────────

        public static string CreateRoom(Document doc, JsonElement args)
        {
            if (!args.TryGetProperty("levelId", out var liProp))
                return "{\"error\":\"levelId is required\"}";
            if (!args.TryGetProperty("point", out var ptProp))
                return "{\"error\":\"point is required\"}";

            var level = doc.GetElement(GetId(liProp.GetInt32())) as Level;
            if (level == null) return "{\"error\":\"Level not found\"}";

            XYZ pt = MmToFt(ptProp);

            using var tx = new Transaction(doc, "MCP: Create Room");
            tx.Start();
#pragma warning disable CS0618
            var room = doc.Create.NewRoom(level, new UV(pt.X, pt.Y));
#pragma warning restore CS0618
            if (args.TryGetProperty("name",   out var nm) && nm.GetString() is string rn) room.Name   = rn;
            if (args.TryGetProperty("number", out var nb) && nb.GetString() is string rk) room.Number = rk;
            tx.Commit();

            return JsonSerializer.Serialize(new
            {
                status = "Success",
                id     = room.Id.Value,
                name   = room.Name,
                number = room.Number,
            });
        }

        // ── create_surface_based_element ─────────────────────────────────────────

        public static string CreateSurfaceBasedElement(Document doc, JsonElement args)
        {
            if (!args.TryGetProperty("familyTypeId", out var ftProp))
                return "{\"error\":\"familyTypeId is required\"}";
            if (!args.TryGetProperty("levelId", out var liProp))
                return "{\"error\":\"levelId is required\"}";
            if (!args.TryGetProperty("boundary", out var boundProp) ||
                boundProp.ValueKind != JsonValueKind.Array)
                return "{\"error\":\"boundary array is required\"}";

            var level = doc.GetElement(GetId(liProp.GetInt32())) as Level;
            if (level == null) return "{\"error\":\"Level not found\"}";

            var floorType = doc.GetElement(GetId(ftProp.GetInt32())) as FloorType;
            if (floorType == null) return "{\"error\":\"Floor type not found\"}";

            var pts = boundProp.EnumerateArray().Select(MmToFt).ToList();
            if (pts.Count < 3) return "{\"error\":\"boundary must have at least 3 points\"}";

            var curveLoop = new CurveLoop();
            for (int i = 0; i < pts.Count; i++)
                curveLoop.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));

            using var tx = new Transaction(doc, "MCP: Create Surface Element");
            tx.Start();
            var floor = Floor.Create(doc, new List<CurveLoop> { curveLoop }, floorType.Id, level.Id);
            tx.Commit();

            return JsonSerializer.Serialize(new
            {
                status = "Success",
                id     = floor.Id.Value,
            });
        }

        // ── create_structural_framing_system ─────────────────────────────────────

        public static string CreateStructuralFramingSystem(Document doc, JsonElement args)
        {
            if (!args.TryGetProperty("beamFamilyTypeId", out var ftProp))
                return "{\"error\":\"beamFamilyTypeId is required\"}";
            if (!args.TryGetProperty("levelId", out var liProp))
                return "{\"error\":\"levelId is required\"}";
            if (!args.TryGetProperty("gridStart", out var gsProp) ||
                !args.TryGetProperty("gridEnd",   out var geProp))
                return "{\"error\":\"gridStart and gridEnd are required\"}";

            var symbol = doc.GetElement(GetId(ftProp.GetInt32())) as FamilySymbol;
            if (symbol == null) return "{\"error\":\"Beam family type not found\"}";

            var level = doc.GetElement(GetId(liProp.GetInt32())) as Level;
            if (level == null) return "{\"error\":\"Level not found\"}";

            double spacingXFt = args.TryGetProperty("spacingXMm", out var sx) ? sx.GetDouble() / 304.8 : 16.4; // 5000mm
            double spacingYFt = args.TryGetProperty("spacingYMm", out var sy) ? sy.GetDouble() / 304.8 : 16.4;
            double elev       = level.Elevation;

            XYZ start = MmToFt(gsProp);
            XYZ end   = MmToFt(geProp);
            double minX = Math.Min(start.X, end.X), maxX = Math.Max(start.X, end.X);
            double minY = Math.Min(start.Y, end.Y), maxY = Math.Max(start.Y, end.Y);

            using var tx = new Transaction(doc, "MCP: Create Framing System");
            tx.Start();
            if (!symbol.IsActive) symbol.Activate();

            var created = new List<long>();
            // Beams in X direction
            for (double y = minY; y <= maxY + 0.001; y += spacingYFt)
            {
                var ln = Line.CreateBound(new XYZ(minX, y, elev), new XYZ(maxX, y, elev));
                var b = doc.Create.NewFamilyInstance(ln, symbol, level, StructuralType.Beam);
                created.Add(b.Id.Value);
            }
            // Beams in Y direction
            for (double x = minX; x <= maxX + 0.001; x += spacingXFt)
            {
                var ln = Line.CreateBound(new XYZ(x, minY, elev), new XYZ(x, maxY, elev));
                var b = doc.Create.NewFamilyInstance(ln, symbol, level, StructuralType.Beam);
                created.Add(b.Id.Value);
            }
            tx.Commit();

            return JsonSerializer.Serialize(new
            {
                status       = "Success",
                beamsCreated = created.Count,
                elementIds   = created,
            });
        }

        // ── delete_element ───────────────────────────────────────────────────────

        public static string DeleteElement(Document doc, JsonElement args)
        {
            if (!args.TryGetProperty("elementIds", out var idsProp) ||
                idsProp.ValueKind != JsonValueKind.Array)
                return "{\"error\":\"elementIds array is required\"}";

            var ids = idsProp.EnumerateArray()
                .Select(e => GetId(e.GetInt32()))
                .ToList();

            using var tx = new Transaction(doc, "MCP: Delete Elements");
            tx.Start();
            int deleted = 0;
            var failed  = new List<long>();
            foreach (var id in ids)
            {
                try { doc.Delete(id); deleted++; }
                catch { failed.Add(id.Value); }
            }
            tx.Commit();

            return JsonSerializer.Serialize(new { deleted, failed });
        }

        // ── operate_element ──────────────────────────────────────────────────────

        public static string OperateElement(Document doc, JsonElement args)
        {
            if (!args.TryGetProperty("elementId", out var eidProp))
                return "{\"error\":\"elementId is required\"}";
            if (!args.TryGetProperty("operation", out var opProp))
                return "{\"error\":\"operation is required (move|rotate|copy)\"}";

            var elemId = GetId(eidProp.GetInt32());
            string op  = opProp.GetString() ?? "";

            using var tx = new Transaction(doc, $"MCP: {op} Element");
            tx.Start();

            if (op == "move")
            {
                if (!args.TryGetProperty("translation", out var trProp))
                    return "{\"error\":\"translation {x,y,z} is required for move\"}";
                XYZ vec = MmToFt(trProp);
                ElementTransformUtils.MoveElement(doc, elemId, vec);
                tx.Commit();
                return JsonSerializer.Serialize(new { status = "Success", operation = "move", elementId = elemId.Value });
            }

            if (op == "copy")
            {
                if (!args.TryGetProperty("translation", out var trProp))
                    return "{\"error\":\"translation {x,y,z} is required for copy\"}";
                XYZ vec     = MmToFt(trProp);
                var newIds  = ElementTransformUtils.CopyElement(doc, elemId, vec);
                tx.Commit();
                return JsonSerializer.Serialize(new
                {
                    status    = "Success",
                    operation = "copy",
                    newIds    = newIds.Select(i => i.Value).ToList(),
                });
            }

            if (op == "rotate")
            {
                if (!args.TryGetProperty("angleDeg", out var angProp))
                    return "{\"error\":\"angleDeg is required for rotate\"}";
                if (!args.TryGetProperty("axisPoint", out var apProp))
                    return "{\"error\":\"axisPoint {x,y,z} is required for rotate\"}";

                double angle = angProp.GetDouble() * Math.PI / 180.0;
                XYZ    axPt  = MmToFt(apProp);
                // Default axis is vertical (Z)
                XYZ    axDir = args.TryGetProperty("axisDirection", out var adProp)
                    ? MmToFt(adProp).Normalize()
                    : XYZ.BasisZ;
                var axis = Line.CreateBound(axPt, axPt + axDir);
                ElementTransformUtils.RotateElement(doc, elemId, axis, angle);
                tx.Commit();
                return JsonSerializer.Serialize(new { status = "Success", operation = "rotate" });
            }

            tx.RollBack();
            return $"{{\"error\":\"Unknown operation: {op}. Use move, copy, or rotate\"}}";
        }

        // ── color_elements ───────────────────────────────────────────────────────

        public static string ColorElements(UIApplication app, Document doc, JsonElement args)
        {
            if (!args.TryGetProperty("category", out var catProp))
                return "{\"error\":\"category is required\"}";
            if (!args.TryGetProperty("paramName", out var pnProp))
                return "{\"error\":\"paramName is required\"}";

            string category = catProp.GetString() ?? "";
            string paramName = pnProp.GetString() ?? "";

            Autodesk.Revit.DB.View view;
            if (args.TryGetProperty("viewId", out var viProp))
            {
                view = doc.GetElement(GetId(viProp.GetInt32())) as Autodesk.Revit.DB.View
                    ?? throw new Exception("View not found");
            }
            else
            {
                view = app.ActiveUIDocument?.ActiveView
                    ?? throw new Exception("No active view");
            }

            var elements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null &&
                    string.Equals(e.Category.Name, category, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Group by parameter value and assign colors
            var groups = elements
                .GroupBy(e =>
                {
                    var p = e.LookupParameter(paramName);
                    return p == null ? "(none)"
                        : (p.StorageType == StorageType.String ? p.AsString() : p.AsValueString()) ?? "(empty)";
                })
                .ToList();

            // Generate evenly spaced hues
            var palette = Enumerable.Range(0, groups.Count)
                .Select(i =>
                {
                    double hue = (double)i / groups.Count;
                    return HsvToRgb(hue, 0.8, 0.9);
                })
                .ToList();

            using var tx = new Transaction(doc, "MCP: Color Elements");
            tx.Start();
            for (int i = 0; i < groups.Count; i++)
            {
                var ogs = new OverrideGraphicSettings();
                ogs.SetSurfaceForegroundPatternColor(palette[i]);
                ogs.SetSurfaceForegroundPatternVisible(true);
                foreach (var elem in groups[i])
                    view.SetElementOverrides(elem.Id, ogs);
            }
            tx.Commit();

            var summary = groups.Zip(palette, (g, c) => new
            {
                value        = g.Key,
                count        = g.Count(),
                colorR       = ((Autodesk.Revit.DB.Color)c).Red,
                colorG       = ((Autodesk.Revit.DB.Color)c).Green,
                colorB       = ((Autodesk.Revit.DB.Color)c).Blue,
            }).ToList();

            return JsonSerializer.Serialize(new
            {
                status = "Success",
                groups = summary,
            });
        }

        private static Autodesk.Revit.DB.Color HsvToRgb(double h, double s, double v)
        {
            int   hi = (int)(h * 6) % 6;
            double f  = h * 6 - Math.Floor(h * 6);
            double p  = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
            (double r, double g, double b) = hi switch
            {
                0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t),
                3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q),
            };
            return new Autodesk.Revit.DB.Color((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        // ── create_dimensions ────────────────────────────────────────────────────

        public static string CreateDimensions(UIApplication app, Document doc, JsonElement args)
        {
            if (!args.TryGetProperty("elementIds", out var eidsProp) ||
                eidsProp.ValueKind != JsonValueKind.Array)
                return "{\"error\":\"elementIds array is required (2+ elements to dimension)\"}";

            Autodesk.Revit.DB.View view = app.ActiveUIDocument?.ActiveView
                ?? throw new Exception("No active view");

            var eids = eidsProp.EnumerateArray().Select(e => GetId(e.GetInt32())).ToList();
            if (eids.Count < 2) return "{\"error\":\"At least 2 elementIds required\"}";

            // Build reference array from element faces perpendicular to dimension line
            // Direction: default X axis unless specified
            XYZ direction = args.TryGetProperty("direction", out var dProp)
                ? MmToFt(dProp).Normalize() : XYZ.BasisX;

            var refs = new ReferenceArray();
            foreach (var eid in eids)
            {
                var elem = doc.GetElement(eid);
                if (elem == null) continue;
                var geomOpts = new Options { View = view };
                var geom = elem.get_Geometry(geomOpts);
                if (geom == null) continue;
                foreach (GeometryObject obj in geom)
                {
                    if (obj is Solid solid)
                    {
                        foreach (Autodesk.Revit.DB.Face face in solid.Faces)
                        {
                            var normal = face.ComputeNormal(new UV(0.5, 0.5));
                            if (Math.Abs(normal.DotProduct(direction)) > 0.9)
                            {
                                refs.Append(face.Reference);
                                break;
                            }
                        }
                    }
                    if (refs.Size > 0) break;
                }
            }

            if (refs.Size < 2)
                return "{\"error\":\"Could not find suitable faces to dimension — try different elements or direction\"}";

            // Dimension line passes through midpoint of first two element bboxes
            var bb0 = doc.GetElement(eids[0]).get_BoundingBox(view);
            var bb1 = doc.GetElement(eids[1]).get_BoundingBox(view);
            if (bb0 == null || bb1 == null) return "{\"error\":\"Could not compute bounding boxes\"}";

            XYZ mid0 = (bb0.Min + bb0.Max) * 0.5;
            XYZ mid1 = (bb1.Min + bb1.Max) * 0.5;
            XYZ linePt0 = mid0 + direction.CrossProduct(XYZ.BasisZ).Normalize() * 2;
            XYZ linePt1 = mid1 + direction.CrossProduct(XYZ.BasisZ).Normalize() * 2;
            var dimLine = Line.CreateBound(linePt0, linePt1);

            using var tx = new Transaction(doc, "MCP: Create Dimension");
            tx.Start();
            var dim = doc.Create.NewDimension(view, dimLine, refs);
            tx.Commit();

            return JsonSerializer.Serialize(new { status = "Success", id = dim.Id.Value });
        }

        // ── tag_all_walls ────────────────────────────────────────────────────────

        public static string TagAllWalls(UIApplication app, Document doc, JsonElement args)
        {
            Autodesk.Revit.DB.View view = app.ActiveUIDocument?.ActiveView
                ?? throw new Exception("No active view");

            if (args.TryGetProperty("viewId", out var viProp))
                view = doc.GetElement(GetId(viProp.GetInt32())) as Autodesk.Revit.DB.View ?? view;

            var walls = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .ToList();

            if (walls.Count == 0) return "{\"status\":\"Success\",\"tagged\":0,\"message\":\"No walls in view\"}";

            // Find a wall tag type
            var tagType = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_WallTags)
                .Cast<FamilySymbol>()
                .FirstOrDefault();

            if (tagType == null) return "{\"error\":\"No wall tag family loaded in project\"}";

            using var tx = new Transaction(doc, "MCP: Tag All Walls");
            tx.Start();
            if (!tagType.IsActive) tagType.Activate();

            int tagged = 0;
            foreach (var wall in walls)
            {
                try
                {
                    var loc = wall.Location as LocationCurve;
                    if (loc == null) continue;
                    XYZ mid = loc.Curve.Evaluate(0.5, true);
                    var tagRef = new Reference(wall);
                    IndependentTag.Create(doc, view.Id, tagRef,
                        false, TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, mid);
                    tagged++;
                }
                catch { /* skip walls that can't be tagged */ }
            }
            tx.Commit();

            return JsonSerializer.Serialize(new { status = "Success", tagged });
        }

        // ── tag_all_rooms ────────────────────────────────────────────────────────

        public static string TagAllRooms(UIApplication app, Document doc, JsonElement args)
        {
            Autodesk.Revit.DB.View view = app.ActiveUIDocument?.ActiveView
                ?? throw new Exception("No active view");

            if (args.TryGetProperty("viewId", out var viProp))
                view = doc.GetElement(GetId(viProp.GetInt32())) as Autodesk.Revit.DB.View ?? view;

            var rooms = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .Cast<SpatialElement>()
                .ToList();

            if (rooms.Count == 0) return "{\"status\":\"Success\",\"tagged\":0,\"message\":\"No rooms in view\"}";

            var tagType = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_RoomTags)
                .Cast<FamilySymbol>()
                .FirstOrDefault();

            if (tagType == null) return "{\"error\":\"No room tag family loaded in project\"}";

            using var tx = new Transaction(doc, "MCP: Tag All Rooms");
            tx.Start();
            if (!tagType.IsActive) tagType.Activate();

            int tagged = 0;
            foreach (var room in rooms)
            {
                try
                {
                    var loc = room.Location as LocationPoint;
                    if (loc == null) continue;
                    var pt = new XYZ(loc.Point.X, loc.Point.Y, 0);
                    IndependentTag.Create(doc, view.Id, new Reference(room), false,
                        TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, pt);
                    tagged++;
                }
                catch { /* skip rooms that can't be tagged */ }
            }
            tx.Commit();

            return JsonSerializer.Serialize(new { status = "Success", tagged });
        }
    }
}
