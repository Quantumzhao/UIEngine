# AGENTS

Read `PROJECT_CONTEXT.md` before making substantial changes.

## How To Work In This Repo
- Prefer small, targeted edits.
- Preserve existing patterns unless there is a strong reason to change them, and explain them. 
- Avoid touching generated files unless the task specifically requires it.
- Explain assumptions when project context is incomplete.
- Include verification steps after making changes.

## Collaboration Preferences
- Always ask for clarification if my intent is ambiguous.
- If there exists a reasonable assumption, keep moving but inform me.
- When several implementation paths exist, briefly explain the tradeoffs.
- For reviews, prioritize bugs, regressions, and missing tests.

## Code Change Guidelines
- Keep changes scoped to the task.
- Never refactor code by yourself. Inform me if it is helpful and let me decide.
- Match existing style and architecture.
- Add comments only when they help explain non-obvious logic.

## Validation Expectations
- Run the smallest meaningful verification possible.
- If full verification is not possible, say what was not checked.
- Call out risks, assumptions, and follow-up work clearly.

## Repo-Specific Notes
- Use `dotnet build` to build the project.
