<!-- Generated from .cursor/rules/documentation-tone.mdc — edit the source, not this file. -->

# Documentation tone

_Scope: `**/*.md`_

Public and in-repo Markdown should read as clear project docs, not marketing or an audit dump.

## Voice

- Calm, factual, and contributor-friendly
- Prefer precise claims over hype (“supports X”, “does not support Y”)
- State defaults, constraints, and failure modes plainly
- Keep security/ops guidance direct and non-dramatic

## Prefer

```markdown
When `X-Comprexy-Conversation-Id` is omitted, identity is derived from the
system prompt and first two user message texts. Sessions that share the same
opening text can map to one stored conversation.
```

## Avoid

```markdown
**Severity:** Critical / High
Adversarial findings… memory bleed… remediation order…
Drop-in compatible with everything…
```

- Audit-style severity labels and postmortem voice in public docs
- Overclaims (“drop-in”, “fully compatible”) without a stated supported surface
- Leaking secrets, local paths, or real request-log contents in examples
- Naming specific IDE products (Cursor, Kilo, …); prefer “IDE”, “IDEs”, or “IDE clients”. Keep literal identifiers when they are filesystem paths (`.cursor/`), mirrored agent layout, or wire/config tool names

## Placement

- Operator/contributor material: `README.md`, `CONTRIBUTING.md`, `docs/`
- Design notes, private reviews, adversarial writeups, and backlog: keep out of the public tree (e.g. gitignored `internal/`)
- Marketing strategy / copy banks: gitignored `internal/marketing-plan.md`; marketing site lives in the `comprexy-cloud` repo (`apps/website/`, tone: that repo’s `.cursor/rules/website.mdc`); do not put slogan copy in OSS docs
- Backlog items (when kept privately): neutral summaries and priorities — not finding dumps

## TODO Tasks

- Never mark a TODO as done if there are still pending items in the same list or section.
- If a TODO is resolved, ensure all related subtasks are also resolved before closing the parent.
