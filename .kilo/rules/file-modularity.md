<!-- Generated from .cursor/rules/file-modularity.mdc — edit the source, not this file. -->

# File modularity for lean AI context

Prefer focused types and files. Do not grow a single source file into a kitchen-sink orchestrator when responsibilities have clear seams.

Optimize for future AI-assisted changes: most edits should require opening only the target collaborator, its shared context/types, and the thin entry type if sequencing or ownership is affected.

After a split, a future change should usually require reading no more than the entry type, the target collaborator, and shared context/types. If a change still requires reading most extracted files, the split did not reduce reasoning complexity.

## Soft limits

Treat these as split triggers, not hard CI gates:

| Signal | Prefer action |
| --- | --- |
| ~400+ lines in one type, or ~600+ in one file | Extract by phase or responsibility before adding more |
| A method past ~150 lines | Extract a named collaborator, or a private helper only for small local mechanics |
| Multiple unrelated `#region`s / phase comments | Those phases are module candidates |

Generated code, frozen fixtures, and pure data tables are exempt when splitting would not improve clarity.

## Planning gate

When a split is triggered, propose the target file tree, responsibility of each file, public exports, and dependency direction before making broad edits, unless the change is small and obvious.

Prefer the smallest stable seam that reduces future context load without increasing navigation cost.

## Do

- Keep a thin entry type only when needed for public API stability, sequencing, or documented ownership boundaries. It may act as a facade, but it must not contain policy guts.
- Split along **phases** or **concerns** already named in the design, such as prepare / complete, materialize / persist, query / command.
- Match existing folder patterns in the same layer.
- Share a small DTO or context record across collaborators instead of duplicating long parameter lists.
- Preserve documented ownership, such as who may flush a unit of work or who holds a lease, on the entry type even when helpers stage work.
- Preserve behavior first. When extracting logic, move code mechanically where possible, then update tests or add focused characterization tests for the affected behavior.
- Prefer one-way dependencies: entry type -> phase/concern collaborators -> shared context/types.
- Avoid cycles between collaborators.
- Keep shared types simple. Shared types may be depended on by many files, but they must not depend on feature collaborators.
- When extending an already-large file, extract first or add the new behavior in a new type in the same change.
- Name extracted files by their domain role, phase, or policy, not by generic terms like helper, common, manager, or util.
- Stop extracting when the remaining code has one clear responsibility, when further splitting would create navigation overhead, or when the seam is not stable enough to name clearly.

## Do not

- Add another major concern to a file that is already past the soft limits.
- Create deep inheritance hierarchies or a second public owner service just to shrink a file.
- Scatter a single transactional or lease boundary across types that each call save/commit independently.
- Split into micro-files with no clear name or seam.
- Create vanity one-liners that make navigation harder without reducing reasoning complexity.
- Use "it was already large" as a reason to make it larger.
- Let collaborators call back into the entry type.
- Move code in a way that changes behavior just to make the file tree look cleaner.
- Create circular dependencies between extracted collaborators.
- Extract abstractions before there are at least two real call sites or a clear architectural boundary.

## Shape

```text
✅  EntryType.cs              # public API, sequence, ownership only
    Feature/
      PhaseA.cs              # focused phase logic
      PhaseB.cs              # focused phase logic
      SharedContext.cs       # small shared state/DTO

❌  EntryType.cs              # prepare + upstream + complete + helpers + DTOs all in one

❌  EntryType.cs
    Feature/
      PhaseAHelper.cs        # vague seam
      CommonStuff.cs         # dumping ground
      MiscManager.cs         # unclear ownership
```
