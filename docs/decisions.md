# docs/decisions.md — Architecture Decision Records

Format: ADR-NNN | Date | Status: Open / Accepted / Superseded

---

## ADR-001 | 2026-03-28 | Accepted
**Keep HTTP POST transport for Phase 1–2, evaluate WebSocket in Phase 3**

Current architecture uses HTTP POST (axios → HttpListener). The reference repo uses WebSocket.

Rationale:
- HTTP POST is working and tested
- Our tool calls are short-lived queries, not streaming — WebSocket advantage is minimal for now
- Migration cost is non-trivial (C# WebSocketServer vs HttpListener)
- Phase 1 goal is tool breadth, not transport performance

Consequences: Must re-evaluate at Phase 3 gate. If tool response times become an issue, WebSocket may be needed.

---

## ADR-002 | 2026-03-28 | Accepted
**ParameterEngine stays in revit-plugin/ for Phase 1**

ParameterEngine.cs is mature, tested, and tightly coupled to App.cs.
Extracting to a separate command set DLL is desirable eventually but not now.

Consequences: Phase 2 refactor will extract it to Commands/ParameterHandler.cs (same DLL, just better organized). Full extraction to a separate DLL deferred to Phase 4.

---

## ADR-003 | 2026-03-28 | Accepted
**send_code_to_revit requires explicit user approval before Phase 3**

This tool executes arbitrary C# inside Revit. Security implications are significant:
- Full Revit API access
- File system access from within Revit
- No sandboxing possible

Requires: security review session with user, explicit decision to proceed, and additional input validation before implementation.

---

## ADR-004 | 2026-03-28 | Accepted
**Tool specs written by Cowork, code written by Claude Code**

No tool is implemented without a complete spec in docs/tool-spec.md.
This prevents ambiguous implementations and ensures the directive layer retains architectural control.

---

## ADR-005 | 2026-03-28 | Open
**AllowedCommands: centralized in App.cs vs distributed in command handlers**

Current: single AllowedCommands list in App.cs (security gate centralized).
Reference repo: each handler manages its own commands.

Tradeoff: Centralized is safer (single security boundary). Distributed is more scalable.
Decision needed: When to migrate if ever.
Recommendation: Keep centralized through Phase 2. Revisit in Phase 3.
