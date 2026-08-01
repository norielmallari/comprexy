<!-- Generated from .cursor/rules/kilo-mirror.mdc — edit the source, not this file. -->

# Kilo mirror

_Scope: `.cursor/rules/**`, `.cursor/agents/**`, `.cursor/skills/**`, `.kilo/**`, `kilo.jsonc`_

`.cursor/` is the **single source of truth** for agent rules, agent prompts, and skills. `.kilo/` is a generated mirror for Kilo Code and carries no independent content.

| Source | Mirror | Loaded by Kilo via |
| --- | --- | --- |
| `.cursor/rules/*.mdc` | `.kilo/rules/*.md` | `instructions` glob in [`kilo.jsonc`](kilo.jsonc) |
| `.cursor/agents/*.md` | `.kilo/agents/*.md` | auto-discovered; filename is the agent name |
| `.cursor/skills/<skill>/**` | `.kilo/skills/<skill>/**` | auto-discovered; loaded on demand |

When you add, remove, rename, or edit a source file, update its mirror in the **same change**.

## Shared, not mirrored

`.cursor/agent-state/<run-folder>/` stays the handoff bus for both tools. Do not fork it to `.kilo/agent-state/`, and do not rewrite agent-state or `.cursor/rules/` paths inside mirrored prompt bodies.

## Rules transform

Kilo appends rule files to the system prompt as plain Markdown. It does **not** parse Cursor frontmatter, and it has no per-file glob activation — everything matched by `instructions` is always loaded.

- Drop the frontmatter block; keep the Markdown body starting at the `#` title
- When the source declares `globs`, restate them in the body as a `_Scope:_` line under the title so applicability survives
- When the source is `alwaysApply: true`, no scope line is needed

## Agents transform

Kilo agent files use their own YAML frontmatter and treat the Markdown body as the system prompt. Keep the body verbatim; translate the frontmatter:

| Cursor | Kilo |
| --- | --- |
| `name:` | dropped — the filename is the agent name |
| `description:` | `description:`, same text, emitted as a **double-quoted** YAML scalar |
| `model: inherit` | omitted — subagents inherit the invoking agent's model |
| `readonly: true` | `permission:` denying `edit`/`write` except `.cursor/agent-state/*` |
| _(no equivalent)_ | `mode:` — `all` for the three orchestrators, `subagent` for specialists |

Quoting `description` is not cosmetic: the source values contain `: ` (e.g. `` `track: backend` ``), which is invalid in a YAML plain scalar even though Cursor tolerates it.

Only orchestrators get `mode: all` so a user can start them directly; specialists stay `mode: subagent` so they are reachable only through the delegation tool or `@name`. Kilo shows each `description` to primary agents, so the roster in [`.cursor/README.md`](.cursor/README.md) needs no separate mirror. [`agent-delegation.mdc`](agent-delegation.mdc) tells the agent when to delegate; this rule only keeps the roster in sync.

### The delegation tool must stay visible

Mirrored agents are unreachable if the model never sees the tool that spawns them. Kilo's is named `task` (Cursor's is `Task`), and the proxy matches denylist entries case-insensitively, so a single `task` entry hides delegation from both clients.

Keep `task` out of `ToolSchema:ExcludeFromModelTools` in [`apps/proxy/appsettings.json`](apps/proxy/appsettings.json). The other Kilo UX tools (`agent_manager`, `agent_manager_models`, `background_process`, `kilo_local_recall`) stay denylisted. To verify against a request trace, the tool must appear in the `model input` catalog, not just `client input`.

## Skills transform

Copy the skill directory verbatim, including frontmatter and supporting files. Insert the banner after the frontmatter block, or on line 1 when the file has none.

## Do

- Keep the `<!-- Generated from ... -->` banner on the first body line of every mirrored file
- Delete the matching mirror when a source file is deleted, and rename both together
- Keep gitignore parity: a source ignored under `.cursor/` (e.g. `rules/*privacy.mdc`) must have its `.kilo/` counterpart ignored too
- Verify parity after the change — the basename sets must match on both sides:

```bash
diff <(ls .cursor/rules | sed 's/\.mdc$//') <(ls .kilo/rules | sed 's/\.md$//')
diff <(ls .cursor/agents) <(ls .kilo/agents)
diff -rq .cursor/skills .kilo/skills   # differences should be banner lines only
```

## Do not

- Edit anything under `.kilo/` as the primary target; author in `.cursor/` and re-transform
- Reword, summarize, or reformat rule prose, agent prompts, or skill bodies while mirroring — only frontmatter handling may differ
- Add a rule, agent, or skill that exists only under `.kilo/`
- Leave Cursor frontmatter in a mirrored rule; Kilo would inject `globs:` and `alwaysApply:` as literal prompt text

## Exception

Mirror-only fixes are allowed when the mirror has already drifted from its source; the fix is to restore the transform output, not to change the source.
