using System;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;

namespace RevitMcpPlugin.Commands
{
    internal static class ModelInfoCommands
    {
        // ── get_current_view_info ────────────────────────────────────────────────

        public static string GetCurrentViewInfo(UIApplication app, Document doc)
        {
            var view = app.ActiveUIDocument?.ActiveView;
            if (view == null) return "{\"error\":\"No active view\"}";

            string? levelName = null;
            long    levelId   = -1;
            if (view is ViewPlan vp && vp.GenLevel != null)
            {
                levelName = vp.GenLevel.Name;
                levelId   = vp.GenLevel.Id.Value;
            }

            return JsonSerializer.Serialize(new
            {
                id           = view.Id.Value,
                name         = view.Name,
                viewType     = view.ViewType.ToString(),
                scale        = view.Scale,
                detailLevel  = view.DetailLevel.ToString(),
                isTemplate   = view.IsTemplate,
                levelId,
                levelName,
            });
        }

        // ── get_current_view_elements ────────────────────────────────────────────

        public static string GetCurrentViewElements(UIApplication app, Document doc, JsonElement args)
        {
            var view = app.ActiveUIDocument?.ActiveView;
            if (view == null) return "{\"error\":\"No active view\"}";

            int maxElements = args.TryGetProperty("maxElements", out var mx) ? mx.GetInt32() : 500;
            string? categoryFilter = args.TryGetProperty("category", out var cf) ? cf.GetString() : null;

            var query = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && !string.IsNullOrEmpty(e.Category.Name));

            if (!string.IsNullOrEmpty(categoryFilter))
                query = query.Where(e =>
                    string.Equals(e.Category!.Name, categoryFilter, StringComparison.OrdinalIgnoreCase));

            var elements = query
                .Take(maxElements)
                .Select(e =>
                {
                    var fi = e as FamilyInstance;
                    return new
                    {
                        id         = e.Id.Value,
                        name       = e.Name,
                        category   = e.Category!.Name,
                        familyName = fi?.Symbol?.FamilyName ?? "",
                        typeName   = fi?.Symbol?.Name
                                     ?? (doc.GetElement(e.GetTypeId()) as ElementType)?.Name
                                     ?? "",
                        levelId    = e.LevelId?.Value ?? -1,
                    };
                })
                .ToList();

            return JsonSerializer.Serialize(new
            {
                viewName     = view.Name,
                elementCount = elements.Count,
                elements,
            });
        }

        // ── get_selected_elements ────────────────────────────────────────────────

        public static string GetSelectedElements(UIApplication app, Document doc)
        {
            var selection = app.ActiveUIDocument?.Selection;
            if (selection == null) return "{\"error\":\"No active UI document\"}";

            var elements = selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .Where(e => e != null)
                .Select(e =>
                {
                    var fi = e as FamilyInstance;
                    return new
                    {
                        id         = e.Id.Value,
                        name       = e.Name,
                        category   = e.Category?.Name ?? "",
                        familyName = fi?.Symbol?.FamilyName ?? "",
                        typeName   = fi?.Symbol?.Name
                                     ?? (doc.GetElement(e.GetTypeId()) as ElementType)?.Name
                                     ?? "",
                    };
                })
                .ToList();

            return JsonSerializer.Serialize(new { selectedCount = elements.Count, elements });
        }

        // ── get_available_family_types ───────────────────────────────────────────

        public static string GetAvailableFamilyTypes(Document doc, JsonElement args)
        {
            string? categoryFilter = args.TryGetProperty("category", out var cf) ? cf.GetString() : null;

            var query = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>();

            if (!string.IsNullOrEmpty(categoryFilter))
                query = query.Where(fs =>
                    string.Equals(fs.Category?.Name ?? "", categoryFilter, StringComparison.OrdinalIgnoreCase));

            var types = query
                .Select(fs => new
                {
                    id         = fs.Id.Value,
                    familyName = fs.FamilyName,
                    typeName   = fs.Name,
                    category   = fs.Category?.Name ?? "",
                    isActive   = fs.IsActive,
                })
                .ToList();

            return JsonSerializer.Serialize(new { count = types.Count, familyTypes = types });
        }

        // ── analyze_model_statistics ─────────────────────────────────────────────

        public static string AnalyzeModelStatistics(Document doc)
        {
            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null)
                .ToList();

            var topCategories = allElements
                .GroupBy(e => e.Category!.Name)
                .OrderByDescending(g => g.Count())
                .Take(20)
                .ToDictionary(g => g.Key, g => g.Count());

            int totalFamilies = new FilteredElementCollector(doc)
                .OfClass(typeof(Family)).GetElementCount();

            int totalTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType().GetElementCount();

            int totalViews = new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .Count(v => !v.IsTemplate);

            int totalSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet)).GetElementCount();

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .Select(l => new
                {
                    name          = l.Name,
                    elevationMm   = Math.Round(l.Elevation * 304.8, 0),
                })
                .ToList();

            return JsonSerializer.Serialize(new
            {
                totalElements  = allElements.Count,
                totalFamilies,
                totalTypes,
                totalViews,
                totalSheets,
                totalLevels    = levels.Count,
                levels,
                topCategories,
            });
        }

        // ── get_material_quantities ──────────────────────────────────────────────

        public static string GetMaterialQuantities(Document doc, JsonElement args)
        {
            string? categoryFilter = args.TryGetProperty("category", out var cf) ? cf.GetString() : null;
            if (string.IsNullOrEmpty(categoryFilter))
                return "{\"error\":\"category is required to scope material quantity extraction\"}";

            IEnumerable<Element> collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null &&
                    string.Equals(e.Category.Name, categoryFilter, StringComparison.OrdinalIgnoreCase));

            var quantities = new List<object>();
            foreach (var elem in collector)
            {
                try
                {
                    foreach (var matId in elem.GetMaterialIds(false))
                    {
                        if (doc.GetElement(matId) is not Material mat) continue;
                        double vol  = elem.GetMaterialVolume(matId);
                        double area = elem.GetMaterialArea(matId, false);
                        if (vol <= 0 && area <= 0) continue;
                        quantities.Add(new
                        {
                            elementId    = elem.Id.Value,
                            category     = elem.Category!.Name,
                            materialName = mat.Name,
                            volumeM3     = Math.Round(vol  * 0.0283168, 6),
                            areaSqM      = Math.Round(area * 0.0929030, 4),
                        });
                    }
                }
                catch { /* skip elements without computable geometry */ }
            }

            return JsonSerializer.Serialize(new { count = quantities.Count, quantities });
        }

        // ── ai_element_filter ────────────────────────────────────────────────────
        // The AI (Claude) constructs structured filter criteria; this method executes them.

        public static string AiElementFilter(Document doc, JsonElement args)
        {
            string? category   = args.TryGetProperty("category",   out var ca) ? ca.GetString() : null;
            string? paramName  = args.TryGetProperty("paramName",  out var pn) ? pn.GetString() : null;
            string? paramValue = args.TryGetProperty("paramValue", out var pv) ? pv.GetString() : null;
            int     maxResults = args.TryGetProperty("maxResults", out var mr) ? mr.GetInt32()  : 200;

            IEnumerable<Element> query = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null);

            if (!string.IsNullOrEmpty(category))
                query = query.Where(e =>
                    string.Equals(e.Category!.Name, category, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(paramName) && !string.IsNullOrEmpty(paramValue))
                query = query.Where(e =>
                {
                    var p = e.LookupParameter(paramName);
                    if (p == null) return false;
                    var val = p.StorageType == StorageType.String
                        ? p.AsString()
                        : p.AsValueString();
                    return val != null && val.IndexOf(paramValue, StringComparison.OrdinalIgnoreCase) >= 0;
                });

            var results = query
                .Take(maxResults)
                .Select(e => new
                {
                    id       = e.Id.Value,
                    name     = e.Name,
                    category = e.Category!.Name,
                    typeName = (doc.GetElement(e.GetTypeId()) as ElementType)?.Name ?? "",
                })
                .ToList();

            return JsonSerializer.Serialize(new { count = results.Count, elements = results });
        }
    }
}
