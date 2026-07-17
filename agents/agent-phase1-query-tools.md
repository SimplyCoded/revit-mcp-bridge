# agents/agent-phase1-query-tools.md — Read-Only Query Tools

> Goal: Add 8 read-only tools and modularize the server.
> All tools in this phase are GET-style — no Revit Transaction required.
> Prerequisite: tool specs written in docs/tool-spec.md before any task starts.

---

## Phase 1 Overview

Phase 1 expands the bridge from 3 tools to 11 (3 existing + 8 new).
All new tools are read-only — safe, no risk of model corruption.
Also includes server refactor: split monolithic `index.ts` into `tools/` modules.

**Tools to add:**
1. `say_hello` — diagnostic, verifies bridge is alive
2. `get_current_view_info` — active view name, type, scale
3. `get_current_view_elements` — all elements in active view
4. `get_selected_elements` — currently selected element IDs + basic info
5. `get_element_by_id` — fetch one element by Revit ElementId
6. `get_available_family_types` — list loadable family types in project
7. `analyze_model_statistics` — element counts by category
8. `ai_element_filter` — filter elements by category, level, type, parameter value

---

## Task 1.0 — Write All Tool Specs (Cowork does this, not Claude Code)

Before any implementation, Cowork writes specs for all 8 tools in `docs/tool-spec.md`.
Template per tool (see orchestrator.md for format).

For each tool, define:
- What Revit API calls are needed (`UIDocument.ActiveView`, `FilteredElementCollector`, etc.)
- Input JSON schema (what the MCP client sends)
- Output JSON schema (what the tool returns)
- C# command string
- Security classification (read-only / requires allowlist)

**Gate:** All 8 specs written and reviewed before Task 1.1 starts.

---

## Task 1.1 — say_hello (diagnostic tool)

**Claude Code prompt:**
```
═══════════════════════════════════════════
CLAUDE CODE TASK — 1-1: say_hello diagnostic tool
═══════════════════════════════════════════

READ FIRST:
  1. CLAUDE.md — "Adding New Commands" section + "Security Model"
  2. docs/tool-spec.md — say_hello entry
  3. server/src/index.ts — understand existing tool registration pattern

CONTEXT:
  The bridge has 3 working tools. We're adding say_hello as a zero-risk
  diagnostic. It opens a Revit TaskDialog with a greeting and returns
  the Revit version and project name. Proves the full stack is alive.

IMPLEMENT:
  C# side (revit-plugin/App.cs):
    - Add "say_hello" to AllowedCommands list
    - Add case "say_hello" to McpCommandHandler.Execute():
        Show TaskDialog.Show("MCP Bridge", "Hello from Claude!")
        Return: { "status": "ok", "revit_version": app.VersionNumber,
                  "project": doc.Title }

  TypeScript side (server/src/index.ts):
    - Register tool in ListToolsRequestSchema: name, description, empty inputSchema
    - Implement in CallToolRequestSchema: POST to plugin, return formatted response

FILES:
  - revit-plugin/App.cs — add to AllowedCommands + Execute() switch
  - server/src/index.ts — register + implement

CONSTRAINTS:
  - No new npm dependencies
  - Error response: { isError: true, content: [{ type: 'text', text: msg }] }
  - The TaskDialog is intentional — it visually confirms the bridge works in Revit

DONE WHEN:
  - npm run build passes
  - Tool appears in tool list when MCP client runs list_tools
  - (Manual test note in PR: "Tested — TaskDialog appeared in Revit")

DO NOT:
  - Add input parameters (this tool takes none)
  - Skip the TaskDialog — it is the diagnostic signal
```

---

## Task 1.2 — Server Modularization (refactor before adding more tools)

**Claude Code prompt:**
```
═══════════════════════════════════════════
CLAUDE CODE TASK — 1-2: Modularize server/src/index.ts
═══════════════════════════════════════════

READ FIRST:
  1. CLAUDE.md — "Folder Structure" target layout + "Tech Stack"
  2. server/src/index.ts — understand current structure fully before touching

CONTEXT:
  index.ts is a single file with all 4 tools. Before adding 8 more tools,
  split it into a modular structure. This is a pure refactor — zero behavior change.

IMPLEMENT:
  Create server/src/client/revitClient.ts:
    - Extract the axios POST logic into: postToRevit(command: string, args: object): Promise<any>
    - Read config from ../../config.json (same path as now)
    - Export single function

  Create server/src/tools/parameters.ts:
    - Move validate_parameters tool definition + handler
    - Move apply_parameter_rules tool definition + handler
    - Export: { toolDefinitions: ToolDefinition[], handleCall: (name, args) => Promise }

  Create server/src/tools/modelQuery.ts:
    - Move get_revit_project_info tool definition + handler
    - Move say_hello tool definition + handler (from Task 1.1)
    - Same export shape as parameters.ts

  Update server/src/index.ts:
    - Import from tools/parameters.ts and tools/modelQuery.ts
    - Register all tools from imported definitions
    - Route calls through imported handlers
    - index.ts should be < 80 lines after refactor

FILES:
  - server/src/client/revitClient.ts — NEW
  - server/src/tools/parameters.ts — NEW
  - server/src/tools/modelQuery.ts — NEW
  - server/src/index.ts — MODIFY (significantly shorter)

CONSTRAINTS:
  - Zero behavior change — existing tools must work identically
  - All Zod schemas must stay with their tool definition (not in index.ts)
  - TypeScript strict mode — no `any`

DONE WHEN:
  - npm run build passes
  - Existing 4 tools still registered and callable (verify with list_tools)
  - index.ts is < 80 lines
  - No `any` types

DO NOT:
  - Change the HTTP POST logic or auth header
  - Move config.json path resolution
  - Change tool names or schemas
```

---

## Tasks 1.3–1.9 — Query Tool Implementation

For each tool (`get_current_view_info`, `get_current_view_elements`, `get_selected_elements`, `get_element_by_id`, `get_available_family_types`, `analyze_model_statistics`, `ai_element_filter`):

**Pattern per tool:**

```
═══════════════════════════════════════════
CLAUDE CODE TASK — 1-[N]: [tool_name]
═══════════════════════════════════════════

READ FIRST:
  1. CLAUDE.md — "Adding New Commands"
  2. docs/tool-spec.md — [tool_name] entry (Cowork has written this)
  3. server/src/tools/modelQuery.ts — existing pattern to follow
  4. revit-plugin/App.cs — existing command handlers for reference

IMPLEMENT:
  C# — revit-plugin/App.cs:
    Add "[command_name]" to AllowedCommands
    Add case "[command_name]": dispatch to private method
    Implement private [MethodName](Document doc, JObject args): string
      Use: [Revit API calls from tool spec]
      Return: JsonConvert.SerializeObject(new { ... })

  TypeScript — server/src/tools/modelQuery.ts:
    Add tool definition (name, description, inputSchema with Zod)
    Add handler in handleCall switch

FILES:
  - revit-plugin/App.cs
  - server/src/tools/modelQuery.ts

CONSTRAINTS:
  - Read-only: no Transaction, no model writes
  - Serialize element data as: { id, name, category, level, type_name }
  - Limit list responses to 500 elements max (add pagination note in response)
  - Null-safe: doc may have elements without names/levels

DONE WHEN:
  - npm run build passes
  - Tool appears in list_tools
  - [specific output verified against spec]

DO NOT:
  - Create Transactions
  - Return raw Revit objects — serialize to plain JSON only
  - Return more than 500 elements without pagination metadata
```

**Cowork fills in the specific Revit API calls and output shape for each tool from `docs/tool-spec.md` before producing each prompt.**

---

## Phase 1 Completion Gate

- [ ] 1.0 All 8 tool specs in docs/tool-spec.md
- [ ] 1.1 say_hello working end-to-end
- [ ] 1.2 Server modularized — index.ts < 80 lines
- [ ] 1.3 get_current_view_info
- [ ] 1.4 get_current_view_elements
- [ ] 1.5 get_selected_elements
- [ ] 1.6 get_element_by_id
- [ ] 1.7 get_available_family_types
- [ ] 1.8 analyze_model_statistics
- [ ] 1.9 ai_element_filter
- [ ] npm run build passes clean
- [ ] All 11 tools listed by list_tools
- [ ] docs/tool-spec.md complete for all 11 tools
- [ ] CLAUDE.md updated: Phase 1 → ✅
