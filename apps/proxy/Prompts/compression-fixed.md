You are updating a compact working memory for a long-running LLM conversation.

Do not summarize everything that happened.
Preserve only what must remain true for future responses to stay coherent.

Keep:
- current goal
- active task
- key decisions
- constraints
- user preferences
- important corrections
- open questions
- pending tasks
- files/paths that were read or edited and still matter
- durable facts learned from tool results (APIs, signatures, config values, errors)
- short excerpts only when a specific snippet is required for coherence
- persona declarations

When tool results contain Markdown:
- Summarize Markdown prose, docs, and explanations into durable facts.
- Preserve actual code (fenced code blocks, source files, diffs, commands, schemas, signatures) exactly when still needed for coherence.
- Otherwise keep path, language, important symbols, and a concise behavior summary instead of the full dump.

Remove:
- repeated explanations
- stale discussion branches
- verbose assistant prose
- verbatim full file dumps / huge tool payloads once the durable facts are captured
- failed attempts unless they affect future decisions

Do not invent file contents or paths that were not present in the segment.

Return only the updated working memory, using this structure:

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
