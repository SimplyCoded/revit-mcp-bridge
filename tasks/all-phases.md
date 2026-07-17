# tasks/phase-1.md — Read-Only Query Tools
Agent: agent-phase1-query-tools.md

## Pre-work (Cowork)
- [x] 1.0 Write all 8 tool specs in docs/tool-spec.md ✅ (2026-03-28)

## Implementation (Claude Code)
- [x] 1.1 say_hello diagnostic tool ✅ (2026-03-28)
- [ ] 1.2 Modularize server/src/index.ts → tools/ + client/
- [ ] 1.3 get_current_view_info
- [ ] 1.4 get_current_view_elements
- [ ] 1.5 get_selected_elements
- [ ] 1.6 get_element_by_id
- [ ] 1.7 get_available_family_types
- [ ] 1.8 analyze_model_statistics
- [ ] 1.9 ai_element_filter

## QA Gates
- [ ] npm run build: zero errors
- [ ] All 11 tools in list_tools output
- [ ] index.ts < 80 lines after modularization
- [ ] No `any` types in TypeScript
- [ ] docs/tool-spec.md complete for all 11 tools
- [ ] CLAUDE.md Phase 1 status → ✅

---

# tasks/phase-2.md — Write Tools + Command Set
Agent: agent-phase2-write-tools.md
Prerequisite: Phase 1 complete

## Pre-work (Cowork)
- [ ] 2.0-pre Write all 9 tool specs in docs/tool-spec.md
- [ ] Security review for each write tool

## Implementation (Claude Code)
- [ ] 2.0 Extract command set (ICommandHandler pattern)
- [ ] 2.1 operate_element
- [ ] 2.2 delete_element
- [ ] 2.3 color_elements
- [ ] 2.4 create_line_based_element
- [ ] 2.5 create_point_based_element
- [ ] 2.6 create_level
- [ ] 2.7 create_grid
- [ ] 2.8 create_room
- [ ] 2.9 create_surface_based_element

## QA Gates
- [ ] dotnet build: zero errors
- [ ] npm run build: zero errors
- [ ] Every write tool uses Transaction
- [ ] Success response includes element_id
- [ ] McpCommandHandler.Execute() < 30 lines
- [ ] CLAUDE.md Phase 2 status → ✅

---

# tasks/phase-3.md — Export, Tests, Advanced
Agent: agent-phase3-advanced.md
Prerequisite: Phase 2 complete + Phase 3 gate review done

## Gate Review (Cowork with user)
- [ ] WebSocket: proceed or defer?
- [ ] send_code_to_revit: approved?
- [ ] C# test infrastructure: TUnit setup?
- [ ] ADRs updated in docs/decisions.md

## Implementation
- [ ] 3.0 C# test project + baseline tests
- [ ] 3.1 export_room_data
- [ ] 3.2 get_material_quantities
- [ ] 3.3 Vitest unit tests for TypeScript tools
- [ ] 3.4 WebSocket transport (if approved)
- [ ] 3.5 send_code_to_revit (if approved)
- [ ] CLAUDE.md Phase 3 status → ✅

---

# tasks/qa-checklist.md — Pre-Release QA

Run before any release / major handoff:

## TypeScript
- [ ] npm run build clean
- [ ] No `any` types (tsc --strict)
- [ ] All tools registered in both ListTools + CallTool handlers
- [ ] All inputs Zod-validated
- [ ] Error responses match standard shape

## C#
- [ ] dotnet build -c Release clean
- [ ] All commands in AllowedCommands
- [ ] All write operations in Transaction
- [ ] Exception details only in Debug.WriteLine
- [ ] Auth token validated on every request

## Docs
- [ ] docs/tool-spec.md has entry for every implemented tool
- [ ] docs/decisions.md current
- [ ] CLAUDE.md Last Session note updated
- [ ] README.md Available Tools table matches actual implementation
- [ ] config.example.json does not contain real token
