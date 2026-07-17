# agents/agent-phase3-advanced.md — Export, WebSocket, Tests

> Goal: Data export tools, optional WebSocket transport, C# integration tests.
> This phase requires architecture review from Cowork before starting.

---

## Phase 3 Gate — Cowork Review Required

Before Phase 3 starts, Cowork must answer:
1. **WebSocket:** Migrate now or defer to Phase 4? (ADR-001 says defer — confirm or reopen)
2. **send_code_to_revit:** Ready for security review? (Has code execution risk — explicit user approval required)
3. **Test infrastructure:** TUnit + RevitApiTest setup required? Or manual testing sufficient?

Do not start Phase 3 tasks until these are answered and recorded in `docs/decisions.md`.

---

## Tools to Add

### Export
- `export_room_data` — all rooms: name, number, area, level, department
- `get_material_quantities` — volume + area per material across elements

### Database (optional)
- `store_project_data` — write project metadata to local SQLite
- `store_room_data` — write room data to local SQLite
- `query_stored_data` — query stored data by project, room, date

### Advanced (high-risk — explicit approval per tool)
- `send_code_to_revit` — execute C# code snippet in Revit (security review required)
- `create_dimensions` — annotation creation
- `create_structural_framing_system` — grid of beams (complex geometry)
- `tag_all_walls` / `tag_all_rooms` — annotation batch operations

---

## Task 3.0 — C# Test Infrastructure

```
CLAUDE CODE TASK — 3-0: Set up C# test project
READ FIRST: CLAUDE.md, reference repo README "Testing" section
IMPLEMENT: Add tests/commandset/ project using TUnit + Nice3point.TUnit.Revit
           Start with 2 tests: say_hello returns ok, get_revit_project_info returns title
DONE WHEN: dotnet test -c Debug.R25 -r win-x64 tests/commandset runs and passes
```

---

## Phase 3 Completion Gate

- [ ] Phase 3 gate review done, decisions recorded
- [ ] 3.0 C# test project + 2 baseline tests
- [ ] 3.1 export_room_data
- [ ] 3.2 get_material_quantities
- [ ] 3.x send_code_to_revit (only if approved)
- [ ] Vitest unit tests for TypeScript tools
- [ ] WebSocket transport (if approved in gate review)
- [ ] CLAUDE.md Phase 3 → ✅
