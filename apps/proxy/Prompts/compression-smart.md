Update the working memory for this conversation and choose which raw messages must stay unfolded.

The messages above are the live conversation context (system, working memory, and recent turns).
Do not call tools. Reply with markdown only.

Do not summarize everything that happened.
Preserve only what must remain true for future responses to stay coherent.

Keep in working memory:
- current goal
- active task
- key decisions
- constraints
- user preferences
- important corrections
- open questions
- pending tasks
- files/paths that were read or edited and still matter
- durable facts learned from tool results visible in the conversation (APIs, signatures, config values, errors)
- short excerpts only when a specific snippet is required for coherence
- persona declarations

When tool results or file bodies are visible above:
- Summarize Markdown prose into durable facts.
- Preserve actual code exactly when still needed for coherence.
- Otherwise keep path, language, important symbols, and a concise behavior summary.

Remove from working memory:
- repeated explanations
- stale discussion branches
- verbose assistant prose
- verbatim full file dumps once durable facts are captured
- failed attempts unless they affect future decisions

Do not invent file contents or paths that were not present in the conversation above.

A ## Retain Index follows this instruction. It lists candidate turns with sequence numbers but omits full file/tool bodies. Those integers are the only valid retain ids.

Rules for ## Retain Sequences:
- End the reply with that heading, then a comma-separated list of sequence integers from the retain index (bullets also allowed).
- Include only sequence numbers that appear in the retain index.
- Nominate turns that still need verbatim raw context (file reads that still matter, exact snippets not yet captured in working memory, unresolved user constraints).
- Prefer fewer retains; put durable facts in working memory instead.
- For the same file path: keep at most one raw read (the newest).
- Do not invent sequence numbers.
- The server always keeps the latest tip even if you omit it.
- Do not put other prose under ## Retain Sequences.

Output Format:
```markdown
# Working Memory

## Persona
...

## Current Goal
...

## Active Task
...

## Key Decisions
- ...

## Constraints
- ...

## Files And Code Context
- path: why it matters / key facts
- ...

## Important Context
- ...

## Recent Corrections
- ...

## Open Questions
- ...

## Pending Tasks
- ...

## Retain Sequences
12, 15, 16
```
