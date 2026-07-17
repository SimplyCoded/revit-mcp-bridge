# Plan: Bauteilkatalog ↔ Revit MCP Bridge Integration

> **Status:** Draft — 2026-03-29
> **Author:** Claude (Cowork) — for review by Doobsie
> **Scope:** New MCP tools to create Revit sheets from Bauteil JSON and extract wall/floor types back to JSON

---

## 1. What We're Building

Two data flows through the bridge:

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│  FLOW A: JSON → Revit (Sheet Creation)                         │
│  ─────────────────────────────────────                         │
│  Input:  Bauteil JSON (Instanz + Schichten + Anforderungen)    │
│  Output: Revit sheet with title block, layer text boxes,       │
│          detail view with detail items, parameters set         │
│                                                                 │
│  FLOW B: Revit → JSON (Type Extraction)                        │
│  ─────────────────────────────────────                         │
│  Input:  Revit project (all wall/floor types in model)         │
│  Output: JSON with type IDs, layer stacks, materials           │
│          → feeds back into Bauteilkatalog for looping          │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. Flow B — Extraction Tools (Revit → JSON)

These are read-only and safe. I'd recommend building these first since they don't modify anything in Revit and they give us the data we need to validate Flow A.

### Tool 2.1: `get_wall_types`

**Purpose:** Extract all WallType definitions from the active Revit project, including their CompoundStructure layers and materials.

**MCP Tool Name:** `get_wall_types`
**C# Command:** `get_wall_types`

**Input:**
```json
{
  "filter_category": "string | null"    // optional: "Aussenwand", "Innenwand", etc.
  "include_layers": "boolean"           // default: true — include CompoundStructure breakdown
}
```

**Output (per wall type):**
```json
{
  "status": "Success",
  "wallTypes": [
    {
      "typeId": 12345,
      "typeName": "AWK_MH_Aussenwand Holz 320mm",
      "familyName": "Basic Wall",
      "wallFunction": "Exterior",       // from WallType.Function
      "width_mm": 320.0,               // WallType.Width → mm
      "layerCount": 5,
      "layers": [
        {
          "index": 0,
          "function": "Finish1",        // CompoundStructureLayer.Function
          "materialId": 67890,
          "materialName": "Holzfaserplatte",
          "materialClass": "Insulation",
          "thickness_mm": 60.0,
          "isVariable": false
        }
        // ... more layers
      ]
    }
    // ... more wall types
  ]
}
```

**C# Implementation Notes:**
- `FilteredElementCollector(doc).OfClass(typeof(WallType))`
- Access `WallType.GetCompoundStructure()` for layers
- Each `CompoundStructureLayer` has `.Function`, `.MaterialId`, `.Width`
- Convert internal units (feet) → mm via `UnitUtils.ConvertFromInternalUnits`
- Get `Material` by ID for name, class, color

---

### Tool 2.2: `get_floor_types`

**Purpose:** Same as above but for FloorType (Decken, Böden).

**MCP Tool Name:** `get_floor_types`
**C# Command:** `get_floor_types`

**Input:**
```json
{
  "include_layers": "boolean"     // default: true
}
```

**Output:** Same structure as wall types but with `floorTypes` array, `FloorType` family info.

**C# Implementation Notes:**
- `FilteredElementCollector(doc).OfClass(typeof(FloorType))`
- Same `GetCompoundStructure()` pattern
- FloorType doesn't have a "function" enum like WallType, but layers still have `CompoundStructureLayer.Function`

---

### Tool 2.3: `get_roof_types` (optional, if Dach is in scope)

Same pattern for `RoofType`. Mention because your Bauteilkatalog has `kategorie: Dach`.

---

### Tool 2.4: `get_compound_type_by_id`

**Purpose:** Get a single wall/floor/roof type by its ElementId, with full layer detail. Useful for the "loop" workflow — once you have IDs from 2.1/2.2, drill into specific ones.

**MCP Tool Name:** `get_compound_type_by_id`
**C# Command:** `get_compound_type_by_id`

**Input:**
```json
{
  "element_id": 12345,
  "include_material_properties": true   // optional: also return lambda, density, etc.
}
```

**Output:**
```json
{
  "typeId": 12345,
  "typeName": "DEC_CLT_200mm",
  "category": "Floors",
  "width_mm": 200.0,
  "layers": [ /* full detail including material thermal/physical props */ ],
  "materialProperties": [
    {
      "materialId": 67890,
      "name": "Brettsperrholz CLT",
      "density_kg_m3": 470.0,
      "thermalConductivity": 0.12,    // lambda [W/(mK)]
      "materialClass": "Wood"
    }
  ]
}
```

**C# Notes:**
- `doc.GetElement(new ElementId(element_id))`
- Cast to appropriate type, get CompoundStructure
- Access `Material.ThermalAsset` for lambda, density, specific heat
- This gives us what we need for U-Wert and Gewicht calculations in the Bauteilkatalog

---

## 3. Flow A — Sheet Creation Tools (JSON → Revit)

These are **write operations** — each wraps in a `Transaction`. I'd suggest building them as composable primitives, then combining them in a single orchestration tool.

### Tool 3.1: `create_sheet`

**Purpose:** Create a new ViewSheet in Revit with a specified title block family.

**MCP Tool Name:** `create_sheet`
**C# Command:** `create_sheet`

**Input:**
```json
{
  "sheet_number": "720_1",
  "sheet_name": "DEC_CLT_200mm",
  "title_block_family": "A4 Querformat"    // or null → default title block
}
```

**Output:**
```json
{
  "status": "Success",
  "sheetId": 54321,
  "sheetNumber": "720_1",
  "sheetName": "DEC_CLT_200mm"
}
```

**C# Notes:**
- `ViewSheet.Create(doc, titleBlockTypeId)`
- Set `sheet.SheetNumber` and `sheet.Name`
- Find title block family: `FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_TitleBlocks)`
- Wrap in Transaction

---

### Tool 3.2: `set_sheet_parameters`

**Purpose:** Set title block parameters on an existing sheet (Bauherr, Objekt, Plan-Nr, Massstab, etc.).

**MCP Tool Name:** `set_sheet_parameters`
**C# Command:** `set_sheet_parameters`

**Input:**
```json
{
  "sheet_id": 54321,
  "parameters": {
    "Bauherr": "SENN",
    "Objekt": "ZenaAreal_Affoltern",
    "Projekt Nr": "2359",
    "Plan Nr": "722",
    "Massstab": "1:10",
    "Grösse": "A4",
    "Datum": "2026-03-29",
    "Gezeichnet": "DS"
  }
}
```

**Output:**
```json
{
  "status": "Success",
  "setCount": 8,
  "skipped": []          // params that didn't exist on the title block
}
```

**C# Notes:**
- Get the sheet by ID, then find the title block instance on the sheet
- Title block params live on the `FamilyInstance` (the title block), not on the `ViewSheet`
- Some are sheet-level params (Sheet Number, Sheet Name) — handle both
- `FamilyInstance.LookupParameter("Bauherr")?.Set("SENN")`
- Report which params were skipped (param name didn't exist on the family)

---

### Tool 3.3: `create_text_notes_on_sheet`

**Purpose:** Create text boxes on a sheet — used for the layer table (Aufbau), the header, and the Anforderungen block.

**MCP Tool Name:** `create_text_notes_on_sheet`
**C# Command:** `create_text_notes_on_sheet`

**Input:**
```json
{
  "sheet_id": 54321,
  "text_notes": [
    {
      "text": "Aufbau Geschossdecke CLT 200mm\nvon oben nach unten\nGesamtdicke: 200 mm",
      "x_mm": 20,
      "y_mm": 250,
      "width_mm": 150,            // text box width
      "text_type_name": "Header",  // or null → default TextNoteType
      "bold": true,
      "font_size_mm": 3.5
    },
    {
      "text": "1. Bodenbelag / Parkett    15mm\n2. Trittschalldämmung       30mm\n3. Brettsperrholz CLT       200mm\n4. Akustikdecke             25mm",
      "x_mm": 20,
      "y_mm": 220,
      "width_mm": 150,
      "text_type_name": "Body",
      "bold": false,
      "font_size_mm": 2.5
    },
    {
      "text": "Anforderungen:\nStatik: tragend — erfüllt\nBrand: REI60 — Lignum 4.1\nU-Wert: 0.15 W/(m²K)\nLuftschall: 57dB Rw+C\nTrittschall: 56dB Lnw+Ci",
      "x_mm": 20,
      "y_mm": 80,
      "width_mm": 150,
      "text_type_name": "Body",
      "bold": false,
      "font_size_mm": 2.0
    }
  ]
}
```

**Output:**
```json
{
  "status": "Success",
  "createdNotes": [
    { "noteId": 11111, "position": { "x_mm": 20, "y_mm": 250 } },
    { "noteId": 11112, "position": { "x_mm": 20, "y_mm": 220 } },
    { "noteId": 11113, "position": { "x_mm": 20, "y_mm": 80 } }
  ]
}
```

**C# Notes:**
- `TextNote.Create(doc, sheetId, position, width, text, textNoteTypeId)`
- Position in XYZ (convert mm → internal units feet)
- TextNoteType for font/size: find by name or create programmatically
- Could also support `TextNote.Create` with `TextNoteOptions` for more control

---

### Tool 3.4: `create_drafting_view`

**Purpose:** Create an empty drafting view where we'll draw the detail section (layer visualization).

**MCP Tool Name:** `create_drafting_view`
**C# Command:** `create_drafting_view`

**Input:**
```json
{
  "view_name": "720_1 Detail Aufbau",
  "scale": 10                          // 1:10
}
```

**Output:**
```json
{
  "status": "Success",
  "viewId": 77777,
  "viewName": "720_1 Detail Aufbau"
}
```

**C# Notes:**
- `ViewDrafting.Create(doc, viewFamilyTypeId)`
- Set `view.Name` and `view.Scale`
- Get drafting view type: `FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))` where `ViewFamily == ViewFamily.Drafting`

---

### Tool 3.5: `draw_detail_items`

**Purpose:** Draw detail lines and filled regions in a drafting view to visualize the layer assembly (the section/detail drawing of the Bauteil).

**MCP Tool Name:** `draw_detail_items`
**C# Command:** `draw_detail_items`

**Input:**
```json
{
  "view_id": 77777,
  "layers": [
    {
      "name": "Bodenbelag / Parkett",
      "thickness_mm": 15,
      "y_offset_mm": 0,              // cumulative from top
      "width_mm": 200,               // drawing width
      "fill_pattern": "Holz",        // Revit fill pattern name, or null
      "color": { "r": 200, "g": 170, "b": 100 },   // optional override
      "label": true                  // add text label with name + thickness
    },
    {
      "name": "Trittschalldämmung",
      "thickness_mm": 30,
      "y_offset_mm": 15,
      "width_mm": 200,
      "fill_pattern": "Dämmung",
      "color": { "r": 255, "g": 230, "b": 100 },
      "label": true
    }
    // ... more layers
  ],
  "options": {
    "draw_border": true,
    "dimension_line": true,          // add total dimension annotation
    "origin_x_mm": 0,
    "origin_y_mm": 0
  }
}
```

**Output:**
```json
{
  "status": "Success",
  "createdElements": 24,             // lines + regions + text
  "totalHeight_mm": 270
}
```

**C# Notes:**
- Per layer: draw a rectangle of `DetailLine` elements (top, bottom, left, right)
- Fill with `FilledRegion.Create(doc, filledRegionTypeId, viewId, curveLoops)` if fill pattern requested
- Find fill patterns: `FilteredElementCollector(doc).OfClass(typeof(FillPatternElement))`
- Add `TextNote` labels inside or beside each layer rectangle
- Optional: add `DetailLine` dimension indicators on the side
- All coordinates relative to view origin, converted mm → feet

---

### Tool 3.6: `place_view_on_sheet`

**Purpose:** Place a drafting view onto a sheet as a Viewport.

**MCP Tool Name:** `place_view_on_sheet`
**C# Command:** `place_view_on_sheet`

**Input:**
```json
{
  "sheet_id": 54321,
  "view_id": 77777,
  "center_x_mm": 140,    // position on sheet
  "center_y_mm": 160
}
```

**Output:**
```json
{
  "status": "Success",
  "viewportId": 88888
}
```

**C# Notes:**
- `Viewport.Create(doc, sheetId, viewId, centerPoint)`
- Convert mm → feet for the center point

---

### Tool 3.7: `create_bauteil_sheet` (Orchestration Tool)

**Purpose:** The "do everything" tool — takes a full Bauteil JSON and creates the complete sheet in one call. Internally calls the logic of tools 3.1–3.6 in sequence.

This is the main tool you'd call in the loop. The individual tools (3.1–3.6) exist for fine-tuning and debugging.

**MCP Tool Name:** `create_bauteil_sheet`
**C# Command:** `create_bauteil_sheet`

**Input:**
```json
{
  "bauteil": {
    "bezeichnung": "Geschossdecke CLT 200mm",
    "bauteil_nr": "720",
    "seite_nr": "1",
    "kategorie": "Decke",
    "richtung": "von oben nach unten",
    "revit_abbr": "DEC",
    "revit_subtype": "CLT",
    "dicke_total_mm": 270,

    "projekt": {
      "bauherr": "SENN",
      "objekt": "ZenaAreal_Affoltern",
      "projekt_nr": "2359"
    },

    "sheet": {
      "massstab": "1:10",
      "blattgroesse": "A4",
      "gezeichnet_von": "DS",
      "datum": "2026-03-29",
      "title_block_family": "A4 Querformat"
    },

    "schichten": [
      {
        "reihenfolge": 1,
        "bezeichnung": "Bodenbelag / Parkett",
        "dicke_mm": 15,
        "layerType": "homogeneous",
        "fill_pattern": "Holz"
      },
      {
        "reihenfolge": 2,
        "bezeichnung": "Trittschalldämmung",
        "dicke_mm": 30,
        "layerType": "homogeneous",
        "fill_pattern": "Dämmung"
      },
      {
        "reihenfolge": 3,
        "bezeichnung": "Brettsperrholz CLT, 5DL",
        "dicke_mm": 200,
        "layerType": "homogeneous",
        "fill_pattern": "Holz"
      },
      {
        "reihenfolge": 4,
        "bezeichnung": "Akustikdecke",
        "dicke_mm": 25,
        "layerType": "homogeneous",
        "fill_pattern": null
      }
    ],

    "anforderungen": {
      "statik": { "anforderung": "tragend", "wert": "erfüllt", "erfuellt": true },
      "brand": { "anforderung": "REI60", "wert": "Lignum 4.1 Tab. 436.-1 Var. D", "erfuellt": true },
      "waerme": { "anforderung": "U ≤ 0.17", "wert": "0.15 W/(m²K)", "erfuellt": true },
      "luftschall": { "anforderung": "52 dB", "wert": "57dB, Lignumdata A1101", "erfuellt": true },
      "trittschall": { "anforderung": "53 dB", "wert": "56dB, Lignumdata A1101", "erfuellt": true }
    },

    "berechnungen": {
      "gewicht_kg_m2": 125.5,
      "u_wert": 0.15,
      "ubp_m2": 42.3,
      "pe_m2": 18.7,
      "thge_m2": 8.2
    }
  }
}
```

**Output:**
```json
{
  "status": "Success",
  "sheetId": 54321,
  "sheetNumber": "720_1",
  "sheetName": "DEC_CLT_200mm",
  "draftingViewId": 77777,
  "viewportId": 88888,
  "textNotesCreated": 3,
  "detailItemsCreated": 24
}
```

**C# Notes:**
- Single Transaction wrapping the entire creation
- Calls internal methods for each step (not separate HTTP calls)
- If any step fails, entire Transaction rolls back — no half-created sheets
- This is the tool you'd call in the "loop" workflow after extraction

---

## 4. Implementation Phases

### Phase A: Extraction (read-only, safe — build first)

| Step | Tool | Effort | Dependencies |
|------|------|--------|-------------|
| A.1 | `get_wall_types` | Medium | None — new C# handler + TS tool |
| A.2 | `get_floor_types` | Low | Reuses A.1 pattern |
| A.3 | `get_compound_type_by_id` | Low | Reuses A.1 pattern |
| A.4 | Test extraction loop | Low | A.1 + A.2 working |

**Deliverable:** JSON file with all wall/floor types, their layers, and material properties. This JSON becomes the seed data for your Bauteilkatalog migration (Section 6 of your plan — "Create Typen from existing data").

### Phase B: Sheet Creation (write operations — build second)

| Step | Tool | Effort | Dependencies |
|------|------|--------|-------------|
| B.1 | `create_sheet` | Medium | Need to know title block family names in your template |
| B.2 | `set_sheet_parameters` | Medium | Need to map WaltGalmarini title block param names |
| B.3 | `create_text_notes_on_sheet` | Medium | Need to decide layout coordinates |
| B.4 | `create_drafting_view` | Low | Straightforward |
| B.5 | `draw_detail_items` | High | Most complex — detail line geometry + fill patterns |
| B.6 | `place_view_on_sheet` | Low | Straightforward |
| B.7 | `create_bauteil_sheet` (orchestrator) | Medium | B.1–B.6 all working |

### Phase C: Loop Workflow (combining A + B)

```
1. Call get_wall_types + get_floor_types → get all types as JSON
2. Map extracted types to Bauteilkatalog Typen
3. For each Instanz in a project:
   a. Enrich JSON with Anforderungen + Berechnungen
   b. Call create_bauteil_sheet → Revit sheet created
4. Validate: all sheets exist, parameters correct
```

---

## 5. What I Need From You Before Implementation

1. **Title block family name** — What's the exact name of your WaltGalmarini title block family in Revit? And what are the exact parameter names on it? (Bauherr, Objekt, etc. — they need to match exactly)

2. **Sheet layout dimensions** — Where exactly should the layer table, detail view, and Anforderungen block be positioned on the A4 sheet? (I can work with approximate mm coordinates, or we iterate)

3. **Fill patterns** — Do you have named fill patterns in your Revit template for materials like "Holz", "Dämmung", "Beton", "Membrane"? Or should we use solid color fills?

4. **Detail drawing style** — Should the layer visualization be simple rectangles with labels? Or do you want hatching patterns matching Revit material standards (e.g., SIA 400 hatch patterns)?

5. **JSON source** — Where will the Bauteil JSON files live? A folder in the project? Generated by the web app? I need to know so the MCP tool knows where to read from.

---

## 6. New AllowedCommands (C# whitelist additions)

```csharp
AllowedCommands = new()
{
    // Existing
    "get_project_info",
    "validate_parameters",
    "apply_parameter_rules",
    "say_hello",

    // Phase A — Extraction
    "get_wall_types",
    "get_floor_types",
    "get_compound_type_by_id",

    // Phase B — Sheet Creation
    "create_sheet",
    "set_sheet_parameters",
    "create_text_notes_on_sheet",
    "create_drafting_view",
    "draw_detail_items",
    "place_view_on_sheet",
    "create_bauteil_sheet",
};
```

---

## 7. Security Considerations

- **Extraction tools (Phase A):** Read-only, no Transaction needed, safe to use freely
- **Sheet creation tools (Phase B):** All wrapped in Transactions, auto-rollback on failure
- **`create_bauteil_sheet`:** Single Transaction for atomicity — either the full sheet is created or nothing is
- **No `send_code_to_revit`:** We're using structured commands, not arbitrary code execution
- **JSON input validation:** Zod schemas on the TS side validate all inputs before they reach C#
- **Rate limiting:** Existing 3-concurrent / 10-queued limits protect Revit from being overwhelmed during batch sheet creation

---

## 8. Relationship to Existing Roadmap

This work fits alongside the existing Phase 1–3 roadmap:

| Existing Phase | Bauteilkatalog Work | Overlap |
|---|---|---|
| Phase 1 (Query tools) | Extraction tools (A.1–A.3) are query tools | High — `get_wall_types` etc. belong in Phase 1 |
| Phase 2 (Write tools) | Sheet creation tools (B.1–B.7) are write operations | High — fits Phase 2 scope |
| Phase 3 (Advanced) | Loop workflow, batch processing | Partial — could be Phase 2.5 |

I'd suggest integrating this into the existing phases rather than creating a parallel track.
