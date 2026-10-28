# Repository instructions

## Git and Branch Safety

- The `main` branch is strictly read-only for automated agents.
- NEVER commit directly to `main`.
- NEVER push directly to `main`.
- NEVER merge a feature branch into `main`.
- NEVER perform a rebase, reset, force push, or history rewrite affecting `main`.
- NEVER update `origin/main`, including through explicit refspecs, GitHub API calls, or GitHub CLI.
- NEVER change the repository's default branch.
- NEVER change or bypass GitHub branch protection rules.
- NEVER use `git push --all`, `git push --mirror`, or commands that could accidentally update `main`.
- NEVER automatically open, merge, or close pull requests.
- NEVER interpret permission to implement a feature as permission to push or merge it.
- The user alone decides when changes enter `main`.

## Feature Branch Workflow

- All application development must take place on a dedicated `feature/*` branch.
- Inspect Git status before modifying files.
- Start from the latest verified `main` without changing its history.
- If the working tree is dirty, preserve existing work and report the issue rather than discarding changes.
- If Phase 4C is not yet present in the intended base branch, stop and report the discrepancy.
- Create the dedicated `feature/*` branch requested for the current task.
- Use `feature/scheduling-constraints` for Phase 5.1.
- Do not reuse an unrelated existing feature branch.
- Commit changes only on the dedicated feature branch for the current task.
- Do not push any branch unless explicitly requested by the user.

## Completion Requirements

- Run relevant builds and tests.
- Report the branch name, commit hash, and working tree status.
- Explicitly report whether any remote branches were updated.
- Stop after implementation and local commit.
- Wait for the user to decide whether to push and open a PR.

## Critical Rule

The user's approval is required before any remote Git operation that changes repository state.
