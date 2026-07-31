<!-- Generated from .cursor/rules/kilo-rules-sync.mdc — edit the source, not this file. -->

# Kilo rules mirror

_Scope: `.cursor/rules/**`, `.kilo/rules/**`, `kilo.jsonc`_

`.cursor/rules/*.mdc` is the **single source of truth** for agent rules. `.kilo/rules/*.md` is a generated mirror for Kilo Code and carries no independent content.

When you add, remove, rename, or edit a rule under `.cursor/rules/`, update the mirror in the **same change**.

## Kilo format constraints

Kilo Code loads rules as plain Markdown appended to the system prompt. It does **not** parse Cursor's YAML frontmatter, and it has no per-file glob activation — every file matched by [`kilo.jsonc`](kilo.jsonc) `instructions` is always loaded.

So the mirror is a transform, not a byte copy:

- Drop the frontmatter block; keep the Markdown body starting at the `#` title
- When the source declares `globs`, restate them in the body as a `_Scope:_` line under the title so the applicability survives
- When the source is `alwaysApply: true`, no scope line is needed
- Keep the `<!-- Generated from ... -->` banner on line 1

## Do

- Write to `.kilo/rules/<same-basename>.md`
- Delete the matching `.md` when a `.mdc` is deleted, and rename both together
- Keep `.kilo/rules/*.md` covered by the `instructions` glob in `kilo.jsonc`
- Keep gitignore parity: a rule ignored under `.cursor/rules/` (e.g. `*privacy.mdc`) must have its `.kilo/rules/` counterpart ignored too
- Verify parity after the change: the basename sets of `.cursor/rules/*.mdc` and `.kilo/rules/*.md` must match

## Do not

- Edit `.kilo/rules/*.md` as the primary target; author in `.cursor/rules/` and re-transform
- Reword, summarize, or reformat rule prose while mirroring — only the frontmatter handling may differ
- Add rules that exist only under `.kilo/rules/`
- Leave frontmatter in a mirrored file; Kilo would inject `globs:` and `alwaysApply:` as literal prompt text

## Exception

Mirror-only fixes are allowed when the mirror has already drifted from its `.mdc` source; the fix is to restore the transform output, not to change the source.
