# agents/orchestrator.md — Orchestration Rules

> This file governs HOW agents run. Every phase agent inherits these rules.

---

## Agent Role

An agent is a Cowork session focused on one phase. It:
- Holds full context for that phase
- Writes tool specs before requesting any implementation
- Produces precise Claude Code prompts
- Validates output before moving to the next task
- Never writes application code itself

---

## Task Prompt Template

```
═══════════════════════════════════════════
CLAUDE CODE TASK — [Phase]-[N]: [Task Name]
═══════════════════════════════════════════

READ FIRST:
  1. CLAUDE.md — [specific sections]
  2. docs/tool-spec.md — [tool name]
  3. [any other relevant file]

CONTEXT: [what exists, what this builds on]

IMPLEMENT: [precise description]

FILES:
  - [path] — [what to do]

CONSTRAINTS:
  - [security, naming, patterns]

DONE WHEN:
  - [verifiable condition]
  - [test that must pass]

DO NOT: [anti-patterns]
```

---

## QA Gate Checklist

Run after every task before proceeding:

**TypeScript:**
- [ ] `cd server && npm run build` — zero errors
- [ ] No `any` types introduced
- [ ] New tool in `ListToolsRequestSchema` handler
- [ ] New tool in `CallToolRequestSchema` handler  
- [ ] Input validated with Zod
- [ ] Error path returns `{ isError: true, content: [{ type: 'text', text: msg }] }`

**C#:**
- [ ] Command string in `AllowedCommands`
- [ ] Case added to `McpCommandHandler.Execute()` switch
- [ ] Revit API calls inside `ExternalEvent` handler only
- [ ] Write operations wrapped in `Transaction` + disposed
- [ ] Exception caught → `Debug.WriteLine` → generic message to caller

**Structural:**
- [ ] `docs/tool-spec.md` has entry for every new tool
- [ ] `docs/decisions.md` updated if architecture decision was made

---

## Handoff Protocol

```
Cowork → Claude Code:
  - Provide: task prompt (template above)
  - Specify: files to read first
  - Specify: exact done condition

Claude Code → Cowork:
  - Report: files created/modified
  - Report: build result (pass/fail + error text)
  - Flag: any ambiguity BEFORE implementing (not after)

Cowork → user (escalation):
  - When: decision changes CLAUDE.md architecture
  - When: security concern found
  - When: task cannot be completed as specified
```

---

## Fix Prompt Template (when QA fails)

```
QA GATE FAILED — [gate name]
Error: [exact message]
File: [path]
Fix only this. Do not change other files unless required.
After fixing, run [build command] and report the result.
```

---

## Phase Agent Index

| Agent | Phase | Scope |
|---|---|---|
| `agent-phase1-query-tools.md` | 1 | Read-only model query tools + server modularization |
| `agent-phase2-write-tools.md` | 2 | Element creation/modification + command set pattern |
| `agent-phase3-advanced.md` | 3 | Export, WebSocket, C# tests |
| `agent-refactor-server.md` | Cross | Modularize index.ts into tools/ directory |
