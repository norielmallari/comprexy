This is an internal Comprexy wrap-up turn. Return **only** working memory markdown.

Rules:
- Do not answer the user.
- Do not call tools.
- Do not mention Comprexy or this wrap-up.
- Use the shared working-memory structure below (`# Working Memory` plus `##` sections).
- An outer markdown fence is optional.
- **Files And Code Context** / **Recent Corrections**: for every path mutated in the fold window (StrReplace / Write / equivalents), pin the **exact current line(s)** (or short tip snippet) after the last successful edit — literal before/after or post-edit text. Do not only paraphrase (“added a cast”, “fixed the mock”). The next hop needs the real string to continue editing without chasing a ghost `old_string`.
- **Rules**: standing operating rules for this conversation (user mandates, safety/ops policies, style, “always/never” instructions that still apply). Carry forward every standing rule from the previous working memory and from user mandates in the fold window. Merge new rules when the user adds them; rewrite a rule only when it is explicitly superseded. Never omit the **Rules** section (use `- None yet.` if empty). Each rule uses `### rule:<normalizedKey>` with title and full body; Comprexy overwrites `## Rules` on accept from the consolidator snapshot.
- **Files And Code Context** entries may also note why a path matters; keep that secondary to literal pins when edits occurred.

Shared working-memory structure:
