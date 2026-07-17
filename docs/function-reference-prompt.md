# Revit MCP Bridge — Function Reference

You have access to the following tools via the Revit MCP bridge. The bridge connects to a live Revit session (C# plugin at 127.0.0.1:8080/revit/) and to local Excel databases. All write operations to Revit are wrapped in Transactions and require an auth token.

**Excel files on disk:**
- `MATERIAL_DB` = `C:/Users/leand/Documents/Projects/Claude/Projects/Material_Database/output/Materialdatenbank_Master.xlsx`
- `BAUTEIL_DB` = `C:/Users/leand/Documents/Projects/Claude/Projects/Bauteilkatalog/data/db_bauteil.xlsx`

---

## 1 — REVIT / PROJECT

```
say_hello()
  → { message: string, version: string, project: string }
  // Diagnostic ping. Opens TaskDialog in Revit. Use to confirm bridge is alive.

get_revit_project_info()
  → { title: string, path: string, rvt_version: string, units: string, discipline: string }
  // Returns active document metadata.

get_levels()
  → Level[]  // { id: int, name: string, elevation_m: number }
  // All levels in the project, sorted by elevation.

get_views()
  → View[]  // { id: int, name: string, type: string, level: string|null, scale: int }
  // All views (floor plans, sections, 3D, drafting, sheets).

get_sheets()
  → Sheet[]  // { id: int, sheet_number: string, sheet_name: string, views: int[] }
  // All sheets in the project with their placed viewport IDs.

get_families()
  → Family[]  // { id: int, name: string, category: string, symbol_count: int }
  // All loaded families grouped by category.

get_family_types(category?: string)
  → FamilyType[]  // { id: int, family: string, name: string, category: string }
  // All FamilySymbol types. Filter by category e.g. "Walls", "Floors", "Title Blocks".

get_current_view_info()
  → { id: int, name: string, type: string, scale: int, level: string|null, discipline: string }
  // Active view metadata.
```

---

## 2 — REVIT / READ ELEMENTS

```
get_wall_types()
  → WallType[]
  // WallType: { id: int, name: string, kind: string, width_m: number,
  //   layers: CompoundLayer[], thermal_resistance: number|null }
  // CompoundLayer: { index: int, function: string, material_name: string,
  //   material_id: int, width_m: number, is_structural: bool }
  // Returns all WallType definitions with full CompoundStructure.

get_floor_types()
  → FloorType[]
  // Same structure as WallType. Returns all FloorType definitions.

get_roof_types()
  → RoofType[]
  // Same structure as WallType. Returns all RoofType/RoofBase definitions.

get_compound_type_by_id(element_id: int)
  → CompoundType
  // CompoundType: { id: int, name: string, category: string, width_m: number,
  //   layers: CompoundLayer[], materials: MaterialDetail[] }
  // MaterialDetail: { name: string, id: int, density: number|null,
  //   lambda: number|null, specific_heat: number|null }
  // Full detail on a single wall/floor/roof type including material asset properties.

get_material_by_id(material_id: int)
  → RevitMaterial
  // RevitMaterial: { id: int, name: string, class: string, category: string,
  //   density: number|null, lambda: number|null, specific_heat: number|null,
  //   color_rgb: [int,int,int], appearance_asset: string|null }

get_all_materials(category?: string)
  → RevitMaterial[]
  // All materials in the project. Optional filter by class e.g. "Wood", "Concrete".

get_selected_elements()
  → Element[]
  // Element: { id: int, name: string, category: string, family: string,
  //   type_id: int, type_name: string, level: string|null,
  //   parameters: Record<string, string|number|bool> }
  // Returns currently selected elements with all instance + type parameters.

get_element_by_id(element_id: int)
  → Element  // Same shape as above.

get_elements_in_view(view_id?: int, category?: string, max?: int)
  → Element[]
  // Elements visible in a view (defaults to active view). Max 500. Filter by category.

get_elements_by_type(type_id: int)
  → Element[]  // All instances of a given FamilySymbol/WallType/etc.

analyze_model_statistics()
  → { categories: Record<string,int>, levels: string[], views: int,
      sheets: int, families: int, materials: int, warnings: int }
  // High-level model health + counts.

get_element_parameters(element_id: int, param_names?: string[])
  → Record<string, { value: string|number|bool, type: string, read_only: bool }>
  // All parameters for an element, or only the requested subset.
```

---

## 3 — REVIT / WRITE ELEMENTS

```
validate_parameters(element_ids: int[], param_names: string[])
  → { valid: bool, results: Record<int, Record<string, { exists: bool, value: any, writable: bool }>> }
  // Check which parameters exist and are writable before attempting writes.

apply_parameter_rules(element_ids: int[], rules: ParameterRule[])
  → { applied: int, skipped: int, errors: string[] }
  // ParameterRule: { type: "set"|"if_then"|"material_summary"|"wall_function_flag",
  //   param: string, value?: any, condition?: string, source_params?: string[] }
  // Apply parameter rules in a single Transaction.

set_element_parameter(element_id: int, param_name: string, value: string|number|bool)
  → { ok: bool, previous_value: any, error?: string }
  // Set a single instance parameter. Wraps in Transaction.

set_element_parameters_batch(updates: { element_id: int, param_name: string, value: any }[])
  → { applied: int, failed: int, errors: { element_id: int, param: string, error: string }[] }
  // Set multiple parameters across multiple elements in one Transaction.

set_type_parameter(type_id: int, param_name: string, value: string|number|bool)
  → { ok: bool, previous_value: any, affected_instances: int, error?: string }
  // Set a type parameter. Affects all instances of that type.

set_material_property(material_id: int, property: string, value: number)
  → { ok: bool, error?: string }
  // Set a material thermal/physical property (density, lambda, specific_heat).
  // property: "density" | "thermal_conductivity" | "specific_heat"

set_material_keynote(material_id: int, kbob_id: string)
  → { ok: bool, error?: string }
  // Set Keynote parameter on a material to the KBOB id_nummer.

set_material_description(material_id: int, description: string)
  → { ok: bool, error?: string }
  // Set Description parameter on a material.
```

---

## 4 — REVIT / SHEETS & VIEWS

```
create_sheet(sheet_number: string, sheet_name: string, title_block_id?: int)
  → { ok: bool, sheet_id: int, error?: string }
  // Create a new ViewSheet. If title_block_id omitted, uses first available title block.

set_sheet_parameters(sheet_id: int, params: {
  bauherr?: string, objekt?: string, projekt_nr?: string, plan_nr?: string,
  massstab?: string, groesse?: string, datum?: string, gezeichnet?: string,
  geprueft?: string, phase?: string, status?: string
})
  → { ok: bool, applied: string[], errors: string[] }
  // Set title block parameters on a sheet.

create_drafting_view(name: string, scale?: int)
  → { ok: bool, view_id: int, error?: string }
  // Create a new empty drafting view. scale defaults to 20.

place_view_on_sheet(sheet_id: int, view_id: int, position_x_mm?: number, position_y_mm?: number)
  → { ok: bool, viewport_id: int, error?: string }
  // Place a view as a Viewport on a sheet. Position in mm from sheet origin.

create_text_note(view_id: int, text: string, x_mm: number, y_mm: number,
  font_size_mm?: number, bold?: bool, width_mm?: number)
  → { ok: bool, element_id: int, error?: string }
  // Create a TextNote in any view (sheet or drafting view).

draw_detail_line(view_id: int, x1_mm: number, y1_mm: number,
  x2_mm: number, y2_mm: number, line_style?: string)
  → { ok: bool, element_id: int, error?: string }
  // Draw a detail line in a drafting view. line_style e.g. "Thin Lines".

draw_filled_region(view_id: int, boundary_pts: [number,number][],
  fill_pattern: string, color_rgb?: [int,int,int])
  → { ok: bool, element_id: int, error?: string }
  // Draw a filled region polygon in a drafting view. boundary_pts in mm.
  // fill_pattern: name of Revit fill pattern e.g. "Wood", "Concrete", "Insulation".

draw_layer_assembly(view_id: int, bauteil: BauteilData,
  origin_x_mm: number, origin_y_mm: number, height_mm?: number)
  → { ok: bool, element_ids: int[], error?: string }
  // Draw full layer cross-section for a Bauteil in a drafting view.
  // Layers drawn left-to-right (Innen→Aussen), proportional widths.
  // BauteilData matches db_bauteil.xlsx Bauteile+Schichten schema.

create_bauteil_sheet(bauteil_id: int, sheet_number: string, sheet_name: string,
  include_anforderungen?: bool)
  → { ok: bool, sheet_id: int, view_id: int, error?: string }
  // Orchestrator: creates sheet → sets title block params → creates drafting view
  // → draws layer assembly → adds text notes (header, layer table, Anforderungen)
  // → places viewport. Single Transaction. bauteil_id references db_bauteil.xlsx.
```

---

## 5 — EXCEL / READ

```
read_sheet(file: "MATERIAL_DB"|"BAUTEIL_DB", sheet: string, filters?: Record<string,any>)
  → Row[]
  // Generic sheet reader. Returns all rows as objects keyed by column header.
  // Optional filters: e.g. { "Match_Status": "Unmatched" } or { "projekt": "Punktbau" }

read_materials(filters?: { produktgruppe?: string, has_lambda?: bool,
  has_kbob?: bool, oeko_zugeordnet?: bool, is_deprecated?: bool })
  → Material[]
  // Material: { material_id, bezeichnung, beschreibung, produktgruppe,
  //   materialkategorie, dichte_kg_m3, lambda, kbob_id_nummer,
  //   oeko_zugeordnet, amortisationszeit_a }
  // Read from MATERIAL_DB / Materialdatenbank sheet (743 rows).

read_kbob_references(filters?: { einheit?: string, id_prefix?: string })
  → KBOBEntry[]
  // KBOBEntry: { id_nummer, bezeichnung, rohdichte, einheit,
  //   ubp_total, thge_total, pe_nicht_erneuerbar, pe_erneuerbar }
  // Read from MATERIAL_DB / KBOB_2022_Ref sheet (391 rows).

read_revit_mapping(filters?: { match_status?: "Matched"|"No KBOB ID",
  revit_sheet?: string })
  → RevitMapping[]
  // RevitMapping: { Revit_Sheet, Revit_Name, Revit_Mark, Keynote_KBOB_ID,
  //   KBOB_Bezeichnung_2022, KBOB_UBP, KBOB_THGE, DB_material_id,
  //   DB_bezeichnung, Match_Status }
  // Read from MATERIAL_DB / Revit_Mapping sheet (156 rows).

read_bauteile(filters?: { typ?: string, projekt?: string })
  → Bauteil[]
  // Bauteil: { bauteil_id, bauteil_nr, bezeichnung, typ, richtung, projekt,
  //   dicke_total_mm, ebkp_code, revit_abbr, revit_subtype, layer_count }
  // Read from BAUTEIL_DB / Bauteile sheet (26 rows).

read_schichten(bauteil_id?: int)
  → Schicht[]
  // Schicht: { schicht_id, bauteil_id, reihenfolge, bezeichnung, material_id,
  //   material_match_status, dicke_mm, breite_mm, sprungmass_mm,
  //   ist_fuellmaterial, layerType, bemerkung }
  // Read from BAUTEIL_DB / Schichten sheet (234 rows). Filter by bauteil_id.

get_bauteil_with_schichten(bauteil_id: int)
  → Bauteil & { schichten: Schicht[] }
  // Single Bauteil with all its layers joined. Convenience wrapper.

search_materials(query: string, field?: "bezeichnung"|"beschreibung"|"produktgruppe"|"all")
  → Material[]  // Full-text search across material fields. Default: all fields.

find_material_by_kbob_id(kbob_id_nummer: string)
  → Material|null  // Find project material linked to a given KBOB id.

find_revit_material_in_db(revit_material_name: string, threshold?: number)
  → { material_id: int, bezeichnung: string, score: number, match_type: string }|null
  // Fuzzy-match a Revit material name against DB_bezeichnung in Revit_Mapping.
  // threshold: 0–1 similarity score minimum (default 0.7).
```

---

## 6 — EXCEL / WRITE

```
update_cell(file: "MATERIAL_DB"|"BAUTEIL_DB", sheet: string,
  row_key: Record<string,any>, column: string, value: any)
  → { ok: bool, row_found: bool, previous_value: any, error?: string }
  // Update a single cell identified by row key (e.g. { material_id: 42 }).

update_row(file: "MATERIAL_DB"|"BAUTEIL_DB", sheet: string,
  row_key: Record<string,any>, updates: Record<string,any>)
  → { ok: bool, columns_updated: string[], error?: string }
  // Update multiple columns in a single row.

update_rows_batch(file: "MATERIAL_DB"|"BAUTEIL_DB", sheet: string,
  updates: { row_key: Record<string,any>, changes: Record<string,any> }[])
  → { ok: bool, updated: int, failed: int, errors: string[] }
  // Batch update many rows in one file open/save cycle.

append_row(file: "MATERIAL_DB"|"BAUTEIL_DB", sheet: string, row: Record<string,any>)
  → { ok: bool, new_row_index: int, error?: string }
  // Append a new row to a sheet. Validates column names against existing headers.

update_schicht_material(schicht_id: int, material_id: int,
  match_status: "matched"|"ambiguous"|"unmatched")
  → { ok: bool, error?: string }
  // Convenience: update material_id + material_match_status on a Schichten row.

update_revit_mapping_status(revit_mark: string, db_material_id: int,
  match_status: "Matched"|"No KBOB ID")
  → { ok: bool, error?: string }
  // Update DB_material_id + Match_Status in Revit_Mapping by Revit_Mark.

write_full_sheet(file: "MATERIAL_DB"|"BAUTEIL_DB", sheet: string,
  rows: Record<string,any>[], mode: "replace"|"append")
  → { ok: bool, rows_written: int, error?: string }
  // Write an entire sheet. mode="replace" overwrites all data rows (keeps header).

add_bauteil(bauteil: Omit<Bauteil,"bauteil_id">, schichten: Omit<Schicht,"schicht_id"|"bauteil_id">[])
  → { ok: bool, bauteil_id: int, schicht_ids: int[], error?: string }
  // Add a new Bauteil + its layers as new rows. Auto-assigns IDs.
```

---

## 7 — VALIDATION

```
validate_sheet_write(file: "MATERIAL_DB"|"BAUTEIL_DB", sheet: string,
  expected_rows: { row_key: Record<string,any>, expected: Record<string,any> }[])
  → ValidationReport
  // Re-reads the sheet and compares expected values against actual.
  // ValidationReport: { ok: bool, checked: int, passed: int, failed: int,
  //   diffs: { row_key: any, field: string, expected: any, actual: any }[] }

validate_parameter_write(element_id: int, param_name: string, expected_value: any)
  → { ok: bool, actual_value: any, match: bool, error?: string }
  // After set_element_parameter, re-reads and confirms the value landed.

validate_batch_parameter_write(updates: { element_id: int, param_name: string, expected: any }[])
  → { ok: bool, passed: int, failed: int,
      diffs: { element_id: int, param: string, expected: any, actual: any }[] }
  // Batch re-read after apply_parameter_rules or set_element_parameters_batch.

validate_material_match_coverage(bauteil_id?: int)
  → { total: int, matched: int, ambiguous: int, unmatched: int,
      unmatched_rows: { schicht_id: int, bezeichnung: string }[] }
  // Reports material_match_status coverage across all Schichten (or one Bauteil).

validate_revit_vs_bauteil(revit_type_id: int, bauteil_id: int)
  → { ok: bool, layer_count_match: bool, width_match: bool,
      diffs: { layer_index: int, field: string, revit: any, excel: any }[] }
  // Compare a Revit WallType/FloorType layer stack against a Bauteil in db_bauteil.xlsx.
  // Checks layer count, total width, individual layer widths and material names.

validate_kbob_links(material_ids?: int[])
  → { total: int, linked: int, missing: int,
      missing_ids: { material_id: int, bezeichnung: string }[] }
  // Check that all materials with kbob_id_nummer resolve to a row in KBOB_2022_Ref.

validate_sheet_structure(file: "MATERIAL_DB"|"BAUTEIL_DB", sheet: string)
  → { ok: bool, expected_columns: string[], missing_columns: string[],
      extra_columns: string[], row_count: int }
  // Verify the sheet exists and has the expected column headers.

diff_excel_vs_revit_materials()
  → { only_in_revit: string[], only_in_excel: string[], matched: int,
      unmatched_revit: string[], coverage_pct: number }
  // Full diff: Revit material names vs DB_bezeichnung in Revit_Mapping.
```

---

## 8 — SYNC / ORCHESTRATION

```
export_wall_types_to_excel()
  → { ok: bool, bauteile_written: int, schichten_written: int,
      unmatched_materials: string[], report: string }
  // Read all WallTypes from Revit → write to BAUTEIL_DB / Bauteile + Schichten.
  // Attempts material matching via find_revit_material_in_db for each layer.
  // Does NOT overwrite rows with material_match_status="matched".

export_floor_types_to_excel()
  → { ok: bool, bauteile_written: int, schichten_written: int,
      unmatched_materials: string[], report: string }
  // Same as above for FloorTypes.

sync_revit_materials_to_mapping()
  → { ok: bool, new_rows: int, updated_rows: int, unmatched: int, report: string }
  // Reads all Revit materials → upserts rows in Revit_Mapping sheet.
  // Matches by name to DB_material_id. Sets Match_Status.

import_bauteil_to_revit_params(bauteil_id: int, element_ids: int[])
  → { ok: bool, applied: int, skipped: int,
      errors: { element_id: int, error: string }[] }
  // Read a Bauteil + Schichten from BAUTEIL_DB → apply layer data as parameters
  // to selected Revit elements. Maps: bezeichnung→Type Name, dicke_mm→Width,
  // material_id→material keynote, ebkp_code→Type Mark.

import_kbob_keynotes_to_materials()
  → { ok: bool, applied: int, skipped: int, errors: string[] }
  // Read Revit_Mapping → for each Matched row, call set_material_keynote
  // with the corresponding Keynote_KBOB_ID.

match_all_unmatched_layers()
  → { ok: bool, newly_matched: int, still_unmatched: int,
      matches: { schicht_id: int, bezeichnung: string, material_id: int, score: number }[] }
  // Fuzzy-match all Schichten rows where material_match_status="unmatched"
  // against the 743-material DB. Updates rows above threshold (0.7).

generate_sync_report()
  → SyncReport
  // SyncReport: { timestamp: string, revit_wall_types: int, revit_materials: int,
  //   excel_bauteile: int, excel_schichten: int, excel_materials: int,
  //   material_coverage_pct: number, unmatched_layers: int,
  //   kbob_coverage_pct: number, sheets_in_revit: int,
  //   diffs: { type: string, description: string, severity: "error"|"warning"|"info" }[] }
  // Full cross-system health check. Run before and after sync operations.

create_all_bauteil_sheets(bauteile?: int[], sheet_number_prefix?: string)
  → { ok: bool, created: int, failed: int,
      sheets: { bauteil_id: int, sheet_id: int, sheet_number: string }[],
      errors: string[] }
  // Batch: for each Bauteil in BAUTEIL_DB (or given IDs), call create_bauteil_sheet.
  // sheet_number_prefix defaults to "BTK-".
```

---

## Type Reference

```
// Shared return types used above

BauteilData = {
  bauteil_id: int, bauteil_nr: string, bezeichnung: string,
  typ: "Aussenwand"|"Innenwand"|"Wohnungstrennwand"|"Decke"|"Dach"|"Boden",
  richtung: string, projekt: string|null, dicke_total_mm: number,
  ebkp_code: string|null, revit_abbr: string|null, revit_subtype: string|null,
  layer_count: int, schichten_preview: string|null
}

Schicht = {
  schicht_id: int, bauteil_id: int, reihenfolge: int, bezeichnung: string,
  material_id: int|null, material_match_status: "matched"|"ambiguous"|"unmatched",
  dicke_mm: number|null, breite_mm: number|null, sprungmass_mm: number|null,
  ist_fuellmaterial: bool, layerType: "homogeneous"|"composite"|"membrane"|"cavity",
  bemerkung: string|null
}

Material = {
  material_id: int, bezeichnung: string, beschreibung: string|null,
  produktgruppe: string|null, materialkategorie: string|null,
  dichte_kg_m3: number|null, lambda: number|null, kbob_id_nummer: string|null,
  oeko_zugeordnet: bool, amortisationszeit_a: number|null
}

KBOBEntry = {
  id_nummer: string, bezeichnung: string, rohdichte: number|null, einheit: string,
  ubp_total: number|null, thge_total: number|null,
  pe_nicht_erneuerbar: number|null, pe_erneuerbar: number|null
}

RevitMapping = {
  Revit_Sheet: string, Revit_Name: string, Revit_Mark: string,
  Keynote_KBOB_ID: string|null, KBOB_Bezeichnung_2022: string|null,
  KBOB_UBP: number|null, KBOB_THGE: number|null,
  DB_material_id: int|null, DB_bezeichnung: string|null,
  Match_Status: "Matched"|"No KBOB ID"
}
```
