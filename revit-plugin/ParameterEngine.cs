using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;

namespace RevitMcpPlugin
{
    /// <summary>
    /// Rule-based parameter validation and auto-fill engine for structural Revit elements.
    ///
    /// Two entry points:
    ///   ValidateParameters  — health report of missing / empty required params
    ///   ApplyParameterRules — suggest or auto-fill values via built-in derived logic
    ///                         and/or caller-supplied if-then rules
    /// </summary>
    public static class ParameterEngine
    {
        // ─────────────────────────────────────────────────────────────────────
        // Supported categories
        // ─────────────────────────────────────────────────────────────────────

        public static readonly Dictionary<string, BuiltInCategory> CategoryMap = new()
        {
            ["OST_StructuralFraming"]    = BuiltInCategory.OST_StructuralFraming,
            ["OST_StructuralColumns"]    = BuiltInCategory.OST_StructuralColumns,
            ["OST_StructuralFoundation"] = BuiltInCategory.OST_StructuralFoundation,
            ["OST_Walls"]                = BuiltInCategory.OST_Walls,
            ["OST_Floors"]               = BuiltInCategory.OST_Floors,
        };

        // ─────────────────────────────────────────────────────────────────────
        // Core schema — built-in parameters always checked per category.
        // Only instance-level BuiltInParameters belong here.
        // If a BIP is not applicable to a specific element, get_Parameter returns
        // null and is silently skipped.
        // ─────────────────────────────────────────────────────────────────────

        private static readonly Dictionary<string, List<(BuiltInParameter Bip, string DisplayName)>> CoreSchema = new()
        {
            ["OST_StructuralFraming"] = new()
            {
                (BuiltInParameter.ALL_MODEL_MARK,              "Mark"),
                (BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "Comments"),
            },
            ["OST_StructuralColumns"] = new()
            {
                (BuiltInParameter.ALL_MODEL_MARK,              "Mark"),
                (BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "Comments"),
            },
            ["OST_StructuralFoundation"] = new()
            {
                (BuiltInParameter.ALL_MODEL_MARK,              "Mark"),
                (BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "Comments"),
            },
            ["OST_Walls"] = new()
            {
                (BuiltInParameter.ALL_MODEL_MARK,              "Mark"),
                (BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "Comments"),
            },
            ["OST_Floors"] = new()
            {
                (BuiltInParameter.ALL_MODEL_MARK,              "Mark"),
                (BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "Comments"),
            },
        };

        // ─────────────────────────────────────────────────────────────────────
        // ValidateParameters
        //
        // Input args:
        //   category          string   required  — one of the keys in CategoryMap
        //   additional_params string[] optional  — custom/shared param names to also check
        //
        // Returns a JSON health report.
        // ─────────────────────────────────────────────────────────────────────

        public static string ValidateParameters(Document doc, JsonElement args)
        {
            if (!args.TryGetProperty("category", out var catElem))
                return Err("'category' is required");

            string category = catElem.GetString() ?? "";
            if (!CategoryMap.TryGetValue(category, out var bic))
                return Err($"Unknown category '{category}'. Valid: {string.Join(", ", CategoryMap.Keys)}");

            // Optional additional custom/shared parameter names
            var additionalParams = new List<string>();
            if (args.TryGetProperty("additional_params", out var apElem) && apElem.ValueKind == JsonValueKind.Array)
                foreach (var item in apElem.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) additionalParams.Add(s!);
                }

            var elements = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElements();

            var issues = new List<ValidationIssue>();
            int passed = 0;

            foreach (var elem in elements)
            {
                var issue = new ValidationIssue
                {
                    ElementId   = elem.Id.Value,
                    ElementName = elem.Name ?? "",
                    Level       = GetLevelName(doc, elem),
                };

                // Core built-in params
                if (CoreSchema.TryGetValue(category, out var coreParams))
                    foreach (var (bip, name) in coreParams)
                    {
                        var p = elem.get_Parameter(bip);
                        if (p == null) continue;          // BIP not applicable to this element
                        if (IsParamEmpty(p)) issue.EmptyParams.Add(name);
                    }

                // Additional custom/shared params
                foreach (var paramName in additionalParams)
                {
                    var p = elem.LookupParameter(paramName);
                    if (p == null)          issue.MissingParams.Add(paramName);
                    else if (IsParamEmpty(p)) issue.EmptyParams.Add(paramName);
                }

                if (issue.MissingParams.Count == 0 && issue.EmptyParams.Count == 0)
                    passed++;
                else
                    issues.Add(issue);
            }

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append($"\"status\":\"Success\",\"category\":{Js(category)},");
            sb.Append($"\"total_elements\":{elements.Count},\"passed\":{passed},\"failed\":{issues.Count},");
            sb.Append("\"issues\":[");
            for (int i = 0; i < issues.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var iss = issues[i];
                sb.Append('{');
                sb.Append($"\"element_id\":{iss.ElementId},");
                sb.Append($"\"element_name\":{Js(iss.ElementName)},");
                sb.Append($"\"level\":{Js(iss.Level)},");
                sb.Append($"\"missing_params\":{JsArr(iss.MissingParams)},");
                sb.Append($"\"empty_params\":{JsArr(iss.EmptyParams)}");
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        // ApplyParameterRules
        //
        // Input args:
        //   category  string   required — one of the keys in CategoryMap
        //   action    string   required — "suggest" | "auto-fill"
        //   rules     array    required — list of rule objects (see rule handlers below)
        //
        // Rule types:
        //   material_summary   — aggregate element materials into a target param
        //   wall_function_flag — set a yes/no param based on Interior vs Exterior wall function
        //   if_then            — set a param when another param equals a given value
        // ─────────────────────────────────────────────────────────────────────

        public static string ApplyParameterRules(Document doc, JsonElement args)
        {
            if (!args.TryGetProperty("category", out var catElem))
                return Err("'category' is required");

            string category = catElem.GetString() ?? "";
            if (!CategoryMap.TryGetValue(category, out var bic))
                return Err($"Unknown category '{category}'");

            string action = args.TryGetProperty("action", out var actionElem)
                ? (actionElem.GetString() ?? "suggest")
                : "suggest";

            if (action != "suggest" && action != "auto-fill")
                return Err("'action' must be 'suggest' or 'auto-fill'");

            var rules = new List<JsonElement>();
            if (args.TryGetProperty("rules", out var rulesElem) && rulesElem.ValueKind == JsonValueKind.Array)
                foreach (var r in rulesElem.EnumerateArray())
                    rules.Add(r);

            if (rules.Count == 0)
                return Err("'rules' array is required and must not be empty");

            // For auto-fill, require an explicit allowlist of writable parameter names.
            // This prevents rules from writing to arbitrary parameters without opt-in.
            HashSet<string>? allowedWriteParams = null;
            if (action == "auto-fill")
            {
                if (!args.TryGetProperty("allowed_write_params", out var awpElem) ||
                    awpElem.ValueKind != JsonValueKind.Array)
                    return Err("'allowed_write_params' (string array) is required when action is 'auto-fill'");

                allowedWriteParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in awpElem.EnumerateArray())
                {
                    var s = p.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) allowedWriteParams.Add(s!);
                }

                if (allowedWriteParams.Count == 0)
                    return Err("'allowed_write_params' must contain at least one parameter name");
            }

            var elements = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .ToElements();

            var suggestions = new List<Suggestion>();
            int appliedCount = 0;

            Transaction? tx = null;
            try
            {
                if (action == "auto-fill")
                {
                    tx = new Transaction(doc, "MCP Parameter Update");
                    tx.Start();
                }

                foreach (var elem in elements)
                    foreach (var rule in rules)
                    {
                        if (!rule.TryGetProperty("type", out var typeElem)) continue;
                        switch (typeElem.GetString())
                        {
                            case "material_summary":
                                ApplyMaterialSummaryRule(doc, elem, rule, action, allowedWriteParams, suggestions, ref appliedCount);
                                break;
                            case "wall_function_flag":
                                if (category == "OST_Walls")
                                    ApplyWallFunctionFlagRule(elem, rule, action, allowedWriteParams, suggestions, ref appliedCount);
                                break;
                            case "if_then":
                                ApplyIfThenRule(doc, elem, rule, action, allowedWriteParams, suggestions, ref appliedCount);
                                break;
                        }
                    }

                if (tx?.GetStatus() == TransactionStatus.Started) tx.Commit();
            }
            catch
            {
                tx?.RollBack();
                throw;
            }
            finally
            {
                tx?.Dispose();
            }

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append($"\"status\":\"Success\",\"action\":{Js(action)},\"category\":{Js(category)},");

            if (action == "suggest")
            {
                sb.Append($"\"total_suggestions\":{suggestions.Count},\"suggestions\":[");
                bool firstGroup = true;
                foreach (var group in suggestions.GroupBy(s => s.ElementId))
                {
                    if (!firstGroup) sb.Append(',');
                    firstGroup = false;
                    sb.Append($"{{\"element_id\":{group.Key},\"element_name\":{Js(group.First().ElementName)},\"changes\":[");
                    bool firstChange = true;
                    foreach (var s in group)
                    {
                        if (!firstChange) sb.Append(',');
                        firstChange = false;
                        sb.Append($"{{\"parameter\":{Js(s.Param)},\"current\":{Js(s.Current)},\"suggested\":{Js(s.Suggested)}}}");
                    }
                    sb.Append("]}");
                }
                sb.Append(']');
            }
            else
            {
                sb.Append($"\"applied_count\":{appliedCount}");
            }

            sb.Append('}');
            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Rule handlers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Aggregates material names from the element's material layer collection into a
        /// single comma-separated string and writes it to the target parameter.
        ///
        /// Rule JSON:
        ///   { "type": "material_summary", "target_param": "Material_Summary" }
        /// </summary>
        private static void ApplyMaterialSummaryRule(
            Document doc, Element elem, JsonElement rule, string action,
            HashSet<string>? allowedWriteParams, List<Suggestion> suggestions, ref int appliedCount)
        {
            string targetParam = rule.TryGetProperty("target_param", out var tp)
                ? (tp.GetString() ?? "Material_Summary") : "Material_Summary";

            var matNames = elem.GetMaterialIds(false)
                .Select(id => doc.GetElement(id) as Material)
                .Where(m => m != null)
                .Select(m => m!.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            if (matNames.Count == 0) return;

            string summary = string.Join(", ", matNames);
            var param = elem.LookupParameter(targetParam);
            string current = param?.AsString() ?? "";

            if (current == summary) return;

            if (action == "auto-fill" && param != null && !param.IsReadOnly &&
                (allowedWriteParams?.Contains(targetParam) ?? false))
            {
                param.Set(summary);
                appliedCount++;
            }
            else
            {
                suggestions.Add(new Suggestion(elem.Id.Value, elem.Name ?? "", targetParam, current, summary));
            }
        }

        /// <summary>
        /// Sets a yes/no parameter based on whether the wall's type function is Exterior.
        ///
        /// Rule JSON:
        ///   {
        ///     "type":           "wall_function_flag",
        ///     "target_param":   "IsExterior",      // default
        ///     "exterior_value": "True",             // default
        ///     "interior_value": "False"             // default
        ///   }
        /// </summary>
        private static void ApplyWallFunctionFlagRule(
            Element elem, JsonElement rule, string action,
            HashSet<string>? allowedWriteParams, List<Suggestion> suggestions, ref int appliedCount)
        {
            if (elem is not Wall wall) return;

            string targetParam   = rule.TryGetProperty("target_param",   out var tp) ? tp.GetString()! : "IsExterior";
            string exteriorValue = rule.TryGetProperty("exterior_value", out var ev) ? ev.GetString()! : "True";
            string interiorValue = rule.TryGetProperty("interior_value", out var iv) ? iv.GetString()! : "False";

            string desired = wall.WallType.Function == WallFunction.Exterior ? exteriorValue : interiorValue;
            var param = elem.LookupParameter(targetParam);
            string current = param?.AsString() ?? "";

            if (current == desired) return;

            if (action == "auto-fill" && param != null && !param.IsReadOnly &&
                (allowedWriteParams?.Contains(targetParam) ?? false))
            {
                param.Set(desired);
                appliedCount++;
            }
            else
            {
                suggestions.Add(new Suggestion(elem.Id.Value, elem.Name ?? "", targetParam, current, desired));
            }
        }

        /// <summary>
        /// Sets a parameter when another parameter equals a specified value.
        /// Condition parameter can be a BuiltInParameter name or a custom param name.
        ///
        /// Rule JSON:
        ///   {
        ///     "type":       "if_then",
        ///     "if_param":   "FUNCTION_PARAM",  // BuiltInParameter name OR custom param name
        ///     "if_value":   "Exterior",         // compared case-insensitively
        ///     "set_param":  "SBB_IsExterior",
        ///     "set_value":  "True"
        ///   }
        /// </summary>
        private static void ApplyIfThenRule(
            Document doc, Element elem, JsonElement rule, string action,
            HashSet<string>? allowedWriteParams, List<Suggestion> suggestions, ref int appliedCount)
        {
            if (!rule.TryGetProperty("if_param",  out var ifP)  ||
                !rule.TryGetProperty("if_value",  out var ifV)  ||
                !rule.TryGetProperty("set_param", out var setP) ||
                !rule.TryGetProperty("set_value", out var setV)) return;

            string ifParam  = ifP.GetString()  ?? "";
            string ifValue  = ifV.GetString()  ?? "";
            string setParam = setP.GetString() ?? "";
            string setValue = setV.GetString() ?? "";

            string actual = GetParamAsString(doc, elem, ifParam);
            if (!string.Equals(actual, ifValue, StringComparison.OrdinalIgnoreCase)) return;

            var param = elem.LookupParameter(setParam);
            string current = param?.AsString() ?? "";

            if (current == setValue) return;

            if (action == "auto-fill" && param != null && !param.IsReadOnly &&
                (allowedWriteParams?.Contains(setParam) ?? false))
            {
                param.Set(setValue);
                appliedCount++;
            }
            else
            {
                suggestions.Add(new Suggestion(elem.Id.Value, elem.Name ?? "", setParam, current, setValue));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static bool IsParamEmpty(Parameter p) => p.StorageType switch
        {
            StorageType.String    => string.IsNullOrWhiteSpace(p.AsString()),
            StorageType.ElementId => p.AsElementId() == ElementId.InvalidElementId,
            _                     => false,   // integers / doubles always carry a value
        };

        /// <summary>
        /// Resolves a parameter name to its string representation.
        /// Tries (in order): special wall-function shorthand, BuiltInParameter enum (instance then
        /// type), and finally LookupParameter by name.
        /// </summary>
        private static string GetParamAsString(Document doc, Element elem, string paramName)
        {
            // Shorthand for wall function
            if (paramName is "FUNCTION_PARAM" or "WallFunction" && elem is Wall wall)
                return wall.WallType.Function.ToString();

            // Try BuiltInParameter by name
            if (Enum.TryParse<BuiltInParameter>(paramName, out var bip))
            {
                var p = elem.get_Parameter(bip);
                if (p != null) return p.AsValueString() ?? p.AsString() ?? "";

                // Fall back to type-level BIP (e.g. WallFunction lives on WallType)
                var typeElem = doc.GetElement(elem.GetTypeId());
                if (typeElem != null)
                {
                    p = typeElem.get_Parameter(bip);
                    if (p != null) return p.AsValueString() ?? p.AsString() ?? "";
                }
            }

            // Named lookup (custom / shared parameters)
            var lp = elem.LookupParameter(paramName);
            return lp?.AsValueString() ?? lp?.AsString() ?? "";
        }

        private static string GetLevelName(Document doc, Element elem)
        {
            var levelId = elem.LevelId;
            if (levelId == null || levelId == ElementId.InvalidElementId) return "";
            return (doc.GetElement(levelId) as Level)?.Name ?? "";
        }

        private static string Err(string msg) =>
            $"{{\"status\":\"Error\",\"error\":{Js(msg)}}}";

        private static string Js(string? s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                           .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
        }

        private static string JsArr(List<string> items) =>
            "[" + string.Join(",", items.Select(Js)) + "]";

        // ─────────────────────────────────────────────────────────────────────
        // Internal data types
        // ─────────────────────────────────────────────────────────────────────

        private sealed class ValidationIssue
        {
            public long           ElementId   { get; init; }
            public string         ElementName { get; init; } = "";
            public string         Level       { get; init; } = "";
            public List<string>   MissingParams { get; } = new();
            public List<string>   EmptyParams   { get; } = new();
        }

        private sealed record Suggestion(
            long   ElementId,
            string ElementName,
            string Param,
            string Current,
            string Suggested);
    }
}
