# docs/tool-spec.md — Tool Input/Output Contracts

> Every tool must have an entry here BEFORE Claude Code implements it.
> Cowork writes specs. Claude Code implements specs.
> Status: Spec | In Progress | Done

---

## Existing Tools (reverse-documented)

---

## Tool: get_revit_project_info
Phase: 0 (existing) | Status: Done

### What it does
Returns the title of the currently open Revit document.

### MCP Input Schema
```json
{}
```
(No inputs)

### MCP Output
```json
{
  "projectName": "string — Revit document title"
}
```

### C# Command Name
`"get_project_info"`

### C# Handler
Returns `doc.Title`

### Required Revit API
- `Document.Title` — read-only property

### Security
Read-only. No Transaction.

---

## Tool: validate_parameters
Phase: 0 (existing) | Status: Done

### What it does
Checks whether required parameters exist and have values on structural elements in a category.

### MCP Input Schema
```json
{
  "category": "string — OST_StructuralFraming | OST_StructuralColumns | OST_StructuralFoundation | OST_Walls | OST_Floors",
  "additional_params": "string[] — optional extra parameter names to check beyond core schema"
}
```

### MCP Output
```json
{
  "total": "number",
  "passed": "number",
  "failed": "number",
  "elements": [
    {
      "id": "number",
      "name": "string",
      "missing_params": "string[]",
      "empty_params": "string[]"
    }
  ]
}
```

### C# Command Name
`"validate_parameters"`

### Security
Read-only. No Transaction.

---

## Tool: apply_parameter_rules
Phase: 0 (existing) | Status: Done

### What it does
Suggests or applies parameter values based on model-derived rules.

### MCP Input Schema
```json
{
  "category": "string",
  "action": "suggest | auto-fill",
  "allowed_write_params": "string[] — REQUIRED if action=auto-fill",
  "rules": [
    {
      "type": "material_summary | wall_function_flag | if_then",
      "target_param": "string",
      "...": "rule-specific fields"
    }
  ]
}
```

### MCP Output
```json
{
  "proposed_changes": [
    { "element_id": "number", "param": "string", "value": "string" }
  ],
  "applied": "boolean",
  "skipped_params": "string[] — params not in allowed_write_params"
}
```

### C# Command Name
`"apply_parameter_rules"`

### Security
`auto-fill` requires `allowed_write_params` allowlist. Any rule targeting unlisted param is skipped silently. Uses Transaction.

---

## Tool: say_hello
Phase: 1 | Status: Done

### What it does
Opens a TaskDialog in Revit ("Hello from [client]!") and returns Revit version + project name. Used to verify the full bridge stack is alive end-to-end.

### MCP Input Schema
```json
{}
```

### MCP Output
```json
{
  "status": "ok",
  "revit_version": "string — e.g. '2025'",
  "project_title": "string"
}
```

### C# Command Name
`"say_hello"`

### C# Handler Method
`HandleSayHello(UIApplication app, Document doc)` in `App.cs`

### Required Revit API
- `TaskDialog.Show(string title, string message)` — shows dialog in Revit UI
- `Application.VersionNumber` — Revit version string
- `Document.Title` — project name

### Security
Read-only. The TaskDialog is intentional — it is the visual confirmation signal.

---

## Tool: get_current_view_info
Phase: 1 | Status: Spec

### What it does
Returns metadata about the currently active view in Revit.

### MCP Input Schema
```json
{}
```

### MCP Output
```json
{
  "view_id": "number",
  "view_name": "string",
  "view_type": "string — FloorPlan | Elevation | Section | ThreeD | Schedule | etc.",
  "scale": "number — view scale denominator (e.g. 100 for 1:100)",
  "level_name": "string | null — associated level if applicable",
  "discipline": "string — Architecture | Structure | Mechanical | Electrical"
}
```

### C# Command Name
`"get_current_view_info"`

### C# Handler Method
`HandleGetCurrentViewInfo(UIApplication app, Document doc)` in `App.cs`

### Required Revit API
- `UIDocument.ActiveView` → `View` object
- `View.Id.IntegerValue` → view_id
- `View.Name` → view_name
- `View.ViewType.ToString()` → view_type
- `View.Scale` → scale
- `View.GenLevel?.Name` → level_name (nullable)
- `View.Discipline.ToString()` → discipline

### Security
Read-only.

---

## Tool: get_current_view_elements
Phase: 1 | Status: Spec

### What it does
Returns all elements visible in the current active view, with basic properties.

### MCP Input Schema
```json
{
  "category_filter": "string | null — optional: filter to one IFC/Revit category name",
  "limit": "number — default 200, max 500"
}
```

### MCP Output
```json
{
  "view_name": "string",
  "total_in_view": "number",
  "returned": "number",
  "elements": [
    {
      "id": "number",
      "name": "string",
      "category": "string",
      "level": "string | null",
      "type_name": "string | null"
    }
  ]
}
```

### C# Command Name
`"get_current_view_elements"`

### C# Handler Method
`HandleGetCurrentViewElements(UIApplication app, Document doc, JObject args)` in `App.cs`

### Required Revit API
- `new FilteredElementCollector(doc, doc.ActiveView.Id)`
- `.WhereElementIsNotElementType()`
- `.OfCategory(BuiltInCategory.X)` if category_filter set
- `.ToElements()` → iterate, serialize

### Security
Read-only. Max 500 elements — return metadata if truncated.

---

## Tool: get_selected_elements
Phase: 1 | Status: Spec

### What it does
Returns the elements currently selected in the Revit UI.

### MCP Input Schema
```json
{}
```

### MCP Output
```json
{
  "count": "number",
  "elements": [
    {
      "id": "number",
      "name": "string",
      "category": "string",
      "level": "string | null",
      "type_name": "string | null",
      "parameters": "object — key/value of instance parameters"
    }
  ]
}
```

### C# Command Name
`"get_selected_elements"`

### C# Handler Method
`HandleGetSelectedElements(UIApplication app, Document doc)` in `App.cs`

### Required Revit API
- `UIDocument.Selection.GetElementIds()` → ICollection<ElementId>
- `doc.GetElement(id)` for each → Element

### Security
Read-only.

---

## Tool: get_element_by_id
Phase: 1 | Status: Spec

### What it does
Fetches detailed information about a single element by its Revit ElementId integer.

### MCP Input Schema
```json
{
  "element_id": "number — Revit ElementId integer value"
}
```

### MCP Output
```json
{
  "id": "number",
  "name": "string",
  "category": "string",
  "level": "string | null",
  "type_name": "string | null",
  "location": "object | null — XYZ point or curve endpoints",
  "parameters": "object — all visible parameter name/value pairs"
}
```

### C# Command Name
`"get_element_by_id"`

### C# Handler Method
`HandleGetElementById(Document doc, JObject args)` in `App.cs`

### Required Revit API
- `new ElementId(int)` + `doc.GetElement(id)`
- `element.LookupParameter(name)` or iterate `element.Parameters`
- `element.Location` → `LocationPoint` or `LocationCurve`

### Security
Read-only. Return 404-style error if element not found.

---

## Tool: get_available_family_types
Phase: 1 | Status: Spec

### What it does
Lists all family types (FamilySymbol) loaded in the project, optionally filtered by category.

### MCP Input Schema
```json
{
  "category": "string | null — optional BuiltInCategory name to filter"
}
```

### MCP Output
```json
{
  "total": "number",
  "family_types": [
    {
      "id": "number",
      "family_name": "string",
      "type_name": "string",
      "category": "string"
    }
  ]
}
```

### C# Command Name
`"get_available_family_types"`

### C# Handler Method
`HandleGetAvailableFamilyTypes(Document doc, JObject args)` in `App.cs`

### Required Revit API
- `new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol))`
- `.Cast<FamilySymbol>()` → iterate
- `fs.Family.Name`, `fs.Name`, `fs.Category.Name`

### Security
Read-only.

---

## Tool: analyze_model_statistics
Phase: 1 | Status: Spec

### What it does
Returns element counts grouped by category across the entire model. Useful for model complexity overview.

### MCP Input Schema
```json
{}
```

### MCP Output
```json
{
  "total_elements": "number",
  "by_category": [
    { "category": "string", "count": "number" }
  ],
  "levels_count": "number",
  "views_count": "number",
  "families_loaded": "number"
}
```

### C# Command Name
`"analyze_model_statistics"`

### C# Handler Method
`HandleAnalyzeModelStatistics(Document doc)` in `App.cs`

### Required Revit API
- `FilteredElementCollector` with `WhereElementIsNotElementType()`
- Group by `element.Category?.Name`
- Separate collectors for Level, View, FamilySymbol

### Security
Read-only.

---

## Tool: ai_element_filter
Phase: 1 | Status: Spec

### What it does
Intelligent element filter — find elements matching combinations of category, level, type name, and parameter value. Designed for AI to use without knowing exact Revit API filter syntax.

### MCP Input Schema
```json
{
  "category": "string | null",
  "level_name": "string | null",
  "type_name_contains": "string | null — partial match on type name",
  "parameter_filter": {
    "param_name": "string",
    "value": "string"
  },
  "limit": "number — default 100, max 500"
}
```

### MCP Output
```json
{
  "total_matched": "number",
  "returned": "number",
  "elements": [
    {
      "id": "number",
      "name": "string",
      "category": "string",
      "level": "string | null",
      "type_name": "string | null"
    }
  ]
}
```

### C# Command Name
`"ai_element_filter"`

### C# Handler Method
`HandleAiElementFilter(Document doc, JObject args)` in `App.cs`

### Required Revit API
- `FilteredElementCollector` chained with:
  - `.OfCategory(BuiltInCategory.X)` if category set
  - Post-filter by `element.LevelId` matched to level name
  - Post-filter by `element.GetTypeId()` → type name contains
  - Post-filter by parameter value

### Security
Read-only. All filters applied in-memory after collector — acceptable for model sizes up to ~50k elements.

---

## Template for New Tools

Copy this block for every new tool:

```markdown
## Tool: [tool_name]
Phase: [1/2/3] | Status: Spec | In Progress | Done

### What it does
[1-2 sentences]

### MCP Input Schema
\`\`\`json
{ }
\`\`\`

### MCP Output
\`\`\`json
{ }
\`\`\`

### C# Command Name
`"command_string"`

### C# Handler Method
`HandleMethodName(args)` in `[filename].cs`

### Required Revit API
- `RevitAPI.Method()` — why

### Security
[Read-only / Requires Transaction / Requires allowed_write_params / Requires user approval]
```
