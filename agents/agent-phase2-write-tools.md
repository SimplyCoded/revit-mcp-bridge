# agents/agent-phase2-write-tools.md — Element Creation & Modification

> Goal: Add element creation, modification, and deletion tools.
> ALL tools in this phase modify the Revit model → require Transaction.
> Security gates are stricter. Cowork reviews every tool spec before implementation.

---

## Phase 2 Security Preamble

Every tool in this phase that modifies Revit must:
1. Wrap all writes in `using (Transaction t = new Transaction(doc, "MCP: [action]")) { t.Start(); ... t.Commit(); }`
2. Call `t.RollbackIfError()` or wrap in try/catch with rollback on exception
3. Validate all input before opening a Transaction (fail early, outside the transaction)
4. Return the ElementId of any created/modified element in the response

Claude Code must not skip any of these — the QA gate checks for them.

---

## Tool Groups

### Group A — Element Modification (lower risk, modify existing)
1. `operate_element` — select, hide, isolate, set color by ElementId
2. `delete_element` — delete by ElementId
3. `color_elements` — color-code by parameter value

### Group B — Element Creation (higher risk, creates new geometry)
4. `create_line_based_element` — walls, beams, pipes (start/end point + type)
5. `create_point_based_element` — doors, windows, furniture (insertion point + type)
6. `create_level` — new level at elevation
7. `create_grid` — grid line system

### Group C — Spatial
8. `create_room` — room at point on level
9. `create_surface_based_element` — floor, ceiling, roof by boundary points

---

## Task 2.0 — Command Set Pattern (refactor, before adding tools)

Before adding Phase 2 tools, extract C# command handling into a proper command set.

**Claude Code prompt:**
```
═══════════════════════════════════════════
CLAUDE CODE TASK — 2-0: Extract Command Set
═══════════════════════════════════════════

READ FIRST:
  1. CLAUDE.md — "Target Architecture", "Command Set Pattern"
  2. revit-plugin/App.cs — current McpCommandHandler.Execute() (understand it fully)
  3. The reference repo README — "Command Set" section (for pattern reference only)

CONTEXT:
  McpCommandHandler.Execute() is a growing switch statement.
  Extract into domain handler classes before Phase 2 adds 9 more commands.
  App.cs keeps the HTTP listener, auth, and ExternalEvent setup.
  Command logic moves to revit-plugin/Commands/ classes.

IMPLEMENT:
  Create revit-plugin/Commands/ICommandHandler.cs:
    interface ICommandHandler {
      bool CanHandle(string command);
      string Execute(Document doc, JObject args);
    }

  Create revit-plugin/Commands/ModelInfoHandler.cs:
    Handles: get_revit_project_info, say_hello, get_current_view_info,
             get_current_view_elements, get_selected_elements, get_element_by_id,
             get_available_family_types, analyze_model_statistics, ai_element_filter

  Create revit-plugin/Commands/ParameterHandler.cs:
    Handles: validate_parameters, apply_parameter_rules
    Wraps existing ParameterEngine calls — do not refactor ParameterEngine itself

  Update McpCommandHandler.Execute():
    Build List<ICommandHandler> handlers on construction
    Route: handlers.FirstOrDefault(h => h.CanHandle(cmd))?.Execute(doc, args)
    Unknown command: return error JSON

FILES:
  - revit-plugin/Commands/ICommandHandler.cs — NEW
  - revit-plugin/Commands/ModelInfoHandler.cs — NEW
  - revit-plugin/Commands/ParameterHandler.cs — NEW
  - revit-plugin/App.cs — update McpCommandHandler only

CONSTRAINTS:
  - Zero behavior change — all existing tools work identically
  - Keep AllowedCommands list in App.cs (security gate stays centralized)
  - ParameterEngine.cs is not touched

DONE WHEN:
  - dotnet build passes
  - All existing 11 tools (Phase 1) still work
  - McpCommandHandler.Execute() is < 30 lines

DO NOT:
  - Move AllowedCommands out of App.cs
  - Change ParameterEngine.cs
  - Add new commands in this task (refactor only)
```

---

## Tasks 2.1–2.9 — Write Tool Implementation

Pattern for each write tool:

```
═══════════════════════════════════════════
CLAUDE CODE TASK — 2-[N]: [tool_name]
═══════════════════════════════════════════

READ FIRST:
  1. CLAUDE.md — "Security Model" (non-negotiable rules)
  2. docs/tool-spec.md — [tool_name] complete spec
  3. revit-plugin/Commands/[appropriate handler] — where to add the method

CONTEXT: [what this builds on]

IMPLEMENT:
  C# — Add to revit-plugin/Commands/[ElementHandler.cs]:
    Add "[command_name]" to CanHandle()
    Implement Execute() branch:
      1. Parse + validate args BEFORE opening Transaction
      2. Open Transaction with descriptive name "MCP: [action]"
      3. Execute Revit API calls
      4. Commit or rollback
      5. Return { "element_id": id, "status": "created/modified/deleted" }

  TypeScript — server/src/tools/elements.ts (create this file in first task):
    Add tool definition with Zod input schema
    Add handler

CONSTRAINTS:
  - Transaction required — no model writes outside Transaction
  - Validate all inputs before Transaction.Start()
  - On exception inside Transaction: t.RollbackIfError() + return error
  - Return created element ID in success response

DONE WHEN:
  - dotnet build passes
  - npm run build passes
  - Tool in list_tools
  - Success response includes element_id
  - Error case returns { isError: true, ... }

DO NOT:
  - Open nested Transactions
  - Skip input validation
  - Return Revit internal objects — JSON only
```

---

## Phase 2 Completion Gate

- [ ] 2.0 Command set pattern implemented
- [ ] 2.1 operate_element
- [ ] 2.2 delete_element
- [ ] 2.3 color_elements
- [ ] 2.4 create_line_based_element
- [ ] 2.5 create_point_based_element
- [ ] 2.6 create_level
- [ ] 2.7 create_grid
- [ ] 2.8 create_room
- [ ] 2.9 create_surface_based_element
- [ ] All tool specs in docs/tool-spec.md
- [ ] dotnet build + npm run build pass clean
- [ ] docs/security.md reviewed and updated
- [ ] CLAUDE.md Phase 2 → ✅
