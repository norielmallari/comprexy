<!-- Generated from .cursor/rules/ui-fixtures.mdc — edit the source, not this file. -->

# UI fixtures

_Scope: `**/*.spec`, `**/*.test`, `**/*.ts`, `**/*.tsx`_

Align with [`test-privacy.mdc`](test-privacy.mdc). Fixtures, mock JSON, and sample UI props must not leak real machines.

## Forbidden

- Real home or project paths (`/Users/...`, `/home/...`, `C:\Users\...`)
- Real emails, hostnames, API keys, or conversation ids from production dumps

## Prefer

- Relative paths: `docs/a.md`, `src/Foo.tsx`
- Synthetic absolutes: `/workspace/repo/docs/a.md`, `/tmp/fixture.md`
- Placeholder hosts: `https://example.test/...`, `http://127.0.0.1:8130` only as a documented local mock target

## Mock payloads

Use fixed synthetic conversation ids and token counts. Prefer checked-in `e2e/fixtures/data/` JSON over copying live control-api responses that contain operator paths.
