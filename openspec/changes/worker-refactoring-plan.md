# Tessera.Worker Refactoring Plan

## Goal

Make background repository processing safe across multiple worker instances, preserve cancellation semantics, prevent credential exposure during git operations, and ensure terminal job states accurately reflect what happened.

## Scope

- `src/Tessera.Worker/JobProcessor.cs`
- `src/Tessera.Worker/Pipeline/AnalysisPipeline.cs`
- `src/Tessera.Worker/Program.cs`
- `src/Tessera.Infrastructure/Analysis/GitClient.cs`
- Related Domain/Infrastructure entities and EF configuration if lease fields are required
- Related tests under `tests/Tessera.Integration.Tests`

## Design Decisions

Before implementing decision 1, confirm whether the worker is ever deployed with more than one replica. `docker-compose.yml` currently defines a single `worker` service. If only one instance is ever expected to run, the leasing mechanism is still valuable as a defensive measure against overlapping manual runs and restarts, but decisions 2-4 (cancellation correctness, PR-review suppression, token exposure) are the higher-priority fixes and do not depend on leasing — they can ship independently and sooner.

### 1. Add atomic job claiming and leases

Replace the current read-then-process flow with an atomic claim operation.

The claim should:

- Select a connected repository eligible for processing.
- Update its status from `Pending` to `Cloning` only when it is still `Pending`.
- Assign a unique worker/lease identifier.
- Set a lease expiration and stage timestamp.
- Return the claimed repository only when the conditional update succeeds.
- Prevent two worker instances from processing the same repository.

EF Core's `ExecuteUpdateAsync` does not return the updated row, so implement the claim as: call `ExecuteUpdateAsync` with a `Where` clause on `Status == Pending` (and expired-or-null lease for reclaim) setting the new status, lease id, and expiration; check the returned row count; only on a count of 1, reload that specific repository by id and lease id to continue processing.

This claim mechanism replaces the existing `staleCutoff`/`UpdatedAt`-based bulk reclaim in `JobProcessor.ProcessPendingAsync`; remove that query once lease expiration is authoritative.

Add lease metadata to `Repository` if needed, for example:

- `ProcessingLeaseId`.
- `LeaseExpiresAt`.
- Optionally `WorkerInstanceId` for diagnostics.

Refresh the lease during long-running cloning, parsing, AI, embedding, and overview stages. Reclaim only jobs whose lease has expired. Do not use `UpdatedAt` alone as a lease because it is a general-purpose timestamp and may not be updated during every external operation.

When reclaiming a job, reset stale processing fields consistently:

- Status.
- Lease fields.
- Stage timestamps.
- Cancellation request state.
- Progress counters where appropriate.

### 2. Separate host shutdown from user-requested cancellation

`CancelRequestedException` represents a repository cancellation request, while the worker `stoppingToken` represents application shutdown. They must not be handled identically.

The pipeline should:

- Preserve `Cancelled` for a user-requested repository cancellation.
- Propagate host shutdown cancellation promptly.
- Avoid attempting normal failure persistence with an already-canceled host token.
- Leave the job recoverable through the lease mechanism when shutdown interrupts processing.
- Use a controlled cleanup token only when a terminal status must be persisted during shutdown.

Return an explicit processing result rather than relying only on the pipeline mutating `Repository.Status`, for example:

```csharp
public enum PipelineResult
{
    Completed,
    Cancelled
}
```

A thrown exception should represent an actual failure; host cancellation should remain cancellation.

Changing `ProcessAsync`'s signature affects existing callers, including `EndToEndPipelineTests`, which currently call `await pipeline.ProcessAsync(repo)` and assert on `repo.Status` afterward; update those call sites and assertions alongside this change rather than leaving them to fail the build.

### 3. Do not process PR reviews after canceled analysis

`JobProcessor` currently processes pending PR reviews after `AnalysisPipeline.ProcessAsync` returns. Since the pipeline handles repository cancellation internally, the worker can continue into PR review processing after cancellation.

Only process PR reviews when the analysis completed successfully for the expected head commit. Check the explicit pipeline result and verify the repository is `Completed` before calling `ProcessPendingPrReviewsAsync`.

Add a test proving that canceling during analysis does not post or process a PR review.

### 4. Remove GitHub tokens from git command arguments

Avoid embedding installation tokens in clone URLs.

The current flow constructs a URL containing the token and passes it to git. This can expose credentials through process inspection, diagnostics, or command errors.

Use a protected authentication mechanism instead:

- Temporary `GIT_ASKPASS` script and environment variables; or
- A temporary credential helper/file with restrictive permissions; or
- Git HTTP extra headers configured without including credentials in the remote URL.

Requirements:

- Do not log the token.
- Do not include the token in `GitCommandException` messages.
- Clean up temporary credentials in a `finally` block.
- Preserve support for public repositories and local repository paths.
- Add tests that inspect the constructed git invocation or credential mechanism without asserting on a real secret value.

`GitClient.cs` is also being changed by the Infrastructure plan (concurrent stdout/stderr reading and cancellation-safe process termination). Implement both sets of changes in a single pass over `GitClient.RunAsync` rather than two separate edits, to avoid merge conflicts and duplicated process-handling logic.

### 5. Keep non-critical enrichment failures observable

Embedding and overview generation are intentionally non-fatal to the core analysis, but cancellation and operational failures should not disappear silently.

For embedding and overview generation:

- Re-throw `OperationCanceledException` when the worker token is canceled.
- Log ordinary failures with repository and snapshot identifiers.
- Preserve successful repository completion when enrichment fails if that remains the intended product behavior.
- Record enrichment availability/failure in a structured way if the dashboard needs to distinguish a complete graph from a complete overview.

### 6. Protect work-directory lifecycle

Ensure work directories are deterministic and safe for concurrent repository processing.

Review:

- Path construction from `repo.FullName`.
- Cleanup behavior after failed or canceled jobs.
- Reuse of an existing clone after a prior failed operation.
- Isolation between repositories with similar names.
- Whether a reclaimed job can safely continue using the existing work directory.

Do not delete a work directory belonging to another active lease. Cleanup must be lease-aware.

## Implementation Sequence

1. Add tests for duplicate job claims, stale-job reclaim, host shutdown, user cancellation, and PR-review suppression.
2. Add lease fields and EF configuration/migration if required.
3. Implement atomic repository claiming and lease refresh in `JobProcessor` and the pipeline.
4. Refactor pipeline outcomes so shutdown cancellation, user cancellation, success, and failure are distinct.
5. Prevent PR review processing unless analysis completed successfully.
6. Replace token-in-URL git authentication with a protected credential mechanism.
7. Fix enrichment cancellation and logging behavior.
8. Review work-directory isolation and cleanup under leases.
9. Run focused worker/integration tests, then the full solution test suite.

## Verification

Run from the repository root:

```powershell
dotnet build Tessera.slnx
dotnet test tests/Tessera.Integration.Tests --no-restore
dotnet test tests/Tessera.Domain.Tests --no-restore
```

Required test coverage:

- Two worker instances cannot claim the same pending repository.
- A failed claim does not start pipeline processing.
- A live lease is not reclaimed merely because an external operation is long-running.
- An expired lease is reclaimed and its processing metadata is reset.
- Lease refresh keeps long-running AI and embedding stages owned by the current worker.
- Host shutdown cancellation does not mark a job as a normal analysis failure.
- User cancellation marks the repository `Cancelled` and clears the request flag.
- Canceled analysis does not process or post pending PR reviews.
- GitHub installation tokens are not present in process arguments, logs, or exception messages.
- Temporary git credentials are removed after success, failure, and cancellation.
- Embedding/overview failures are logged but do not falsely report successful enrichment.
- Canceled enrichment propagates cancellation.
- Work directories remain isolated between repositories and worker leases.

## Compatibility Considerations

- Preserve existing repository statuses and dashboard status meanings where possible.
- If lease fields are added, provide defaults for existing rows and migrate safely.
- Preserve retry behavior for failed PR reviews.
- Preserve public repository cloning and local repository analysis.
- Do not change graph, snapshot, or API response formats unless lease state must be exposed for diagnostics.

## Non-Goals

- Do not redesign the analysis algorithm or Domain Merkle implementation.
- Do not replace the worker hosting model.
- Do not redesign API authentication in this plan.
- Do not make embedding or overview generation a hard requirement for graph analysis unless product requirements change.

## Completion Criteria

- Repository work is atomically claimed and lease-owned.
- Multiple worker instances cannot process the same repository concurrently.
- Stale jobs are reclaimed safely without stealing active work.
- Host shutdown and user cancellation have distinct behavior.
- Canceled analysis cannot trigger PR review processing.
- GitHub installation tokens never enter git command arguments or logs.
- Enrichment failures remain observable and cancellation-safe.
- Worker and integration tests pass, including multi-worker and cancellation scenarios.
