---
name: unit-tester
model: auto-smart[optimize_for=cost]
description: Interim Task slug for the track-aware unit/component tester. Prefer `backend-unit-tester` (backend) or `ui-unit-tester` (UI). Full prompts: [`backend-unit-tester.md`](backend-unit-tester.md), [`ui-unit-tester.md`](ui-unit-tester.md).
---

# unit-tester (interim stub)

**Canonical agents:**

| Track | Follow |
| --- | --- |
| backend | [`backend-unit-tester.md`](backend-unit-tester.md) |
| ui | [`ui-unit-tester.md`](ui-unit-tester.md) |

If launched via this slug, resolve `Track` from the assigned versioned handoff / approved plan, then follow that file’s instructions entirely. Prefer the parent launches `backend-unit-tester` or `ui-unit-tester` directly when available.
