<!-- Generated from .cursor/rules/file-modularity.mdc — edit the source, not this file. -->

# File modularity

Prefer focused types and files. Do not grow a single source file into a kitchen-sink orchestrator when responsibilities have clear seams.

## Soft limits

Treat these as split triggers, not hard CI gates:

| Signal | Prefer action |
| --- | --- |
| ~400+ lines in one type, or ~600+ in one file | Extract by phase or responsibility before adding more |
| A method past ~150 lines | Extract a collaborator or private helper type |
| Multiple unrelated `#region`s / phase comments | Those phases are module candidates |

Generated code, frozen fixtures, and pure data tables are exempt when splitting would not improve clarity.

## Do

- Keep a thin entry façade (wire-up, sequencing, documented ownership boundaries) and move policy guts into collaborators
- Split along **phases** or **concerns** already named in the design (prepare / complete, materialize / persist, query / command) — match existing folder patterns in the same layer
- Share a small DTO or context record across collaborators instead of duplicating parameters
- Preserve documented ownership (e.g. who may flush a unit of work, who holds a lease) on the façade even when helpers stage work
- When extending an already-large file, extract first or add the new behavior in a new type in the same change

## Do not

- Add another major concern to a file that is already past the soft limits
- Create deep inheritance hierarchies or a second public “owner” service just to shrink a file
- Scatter a single transactional or lease boundary across types that each call save/commit independently
- Split into micro-files with no clear name or seam (vanity one-liners)
- Use “it was already large” as a reason to make it larger

## Shape

```text
✅  EntryType.cs              # sequence + ownership only
    Feature/
      PhaseA.cs
      PhaseB.cs
      SharedContext.cs

❌  EntryType.cs              # prepare + upstream + complete + helpers + DTOs all in one
```
