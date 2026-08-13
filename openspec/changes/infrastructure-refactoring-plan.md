# Tessera.Infrastructure Refactoring Plan

## Goal

Harden infrastructure adapters and services around filesystem boundaries, cancellation, concurrency, external resources, and configuration refresh behavior without changing public API contracts.

## Scope

- `src/Tessera.Infrastructure/Storage/FileSystemObjectStore.cs`
- `src/Tessera.Infrastructure/Ai/AiSettingsCache.cs`
- `src/Tessera.Infrastructure/Ai/ProviderRegistry.cs`
- `src/Tessera.Infrastructure/Ai/AiSettingsService.cs`
- `src/Tessera.Infrastructure/Chat/RetrievalService.cs`
- `src/Tessera.Infrastructure/Analysis/GitClient.cs`
- `src/Tessera.Infrastructure/GitHub/GitHubAppClient.cs`
- `src/Tessera.Infrastructure/GitHub/GitHubOAuthClient.cs`
- `src/Tessera.Infrastructure/Ai/OpenAiCompatibleChatProvider.cs`
- Related integration tests under `tests/Tessera.Integration.Tests`
- A migration only if the AI primary-provider invariant requires a database index

## Design Decisions

### 1. Secure object-store path resolution

Make object-store keys incapable of escaping the configured root directory.

`FileSystemObjectStore.GetPath` should:

- Reject null, empty, rooted, or invalid keys.
- Normalize the root once with `Path.GetFullPath`.
- Combine the root and key, then normalize the result with `Path.GetFullPath`.
- Verify the normalized path is inside the normalized root, including a directory-separator boundary.
- Reject traversal such as `../file`, `a/../../file`, and Windows-style traversal using backslashes.
- Preserve nested keys such as `snapshots/hash.json`.

Use the same validation for `PutAsync`, `GetAsync`, and `ExistsAsync`. Add tests that prove valid nested keys work and traversal/rooted keys are rejected without creating files outside the root.

### 2. Make AI cache semaphore handling cancellation-safe

Update `AiSettingsCache.RefreshAsync` so `_gate.Release()` runs only when `_gate.WaitAsync(ct)` successfully acquired the semaphore.

The refresh flow should:

- Track whether the semaphore was acquired.
- Re-throw caller cancellation rather than treating it as a stale-cache refresh failure.
- Preserve the previous snapshot for ordinary database/provider refresh failures.
- Reset `_refreshQueued` in a thread-safe way.
- Avoid unobserved task exceptions from the fire-and-forget refresh started by `GetSnapshot` (`_ = RefreshAsync()`); log any exception inside that background call explicitly, since an unobserved faulted task can otherwise surface as an unhandled exception on finalization.

Consider replacing the current `_refreshQueued` flag with a task-based refresh gate or an atomic state transition so concurrent callers cannot schedule duplicate refreshes.

### 3. Preserve cancellation during retrieval and fallback paths

Review broad `catch (Exception)` blocks in retrieval, chat, overview, linking, embedding, and PR-review services.

For every fallback path:

- Re-throw `OperationCanceledException` when the supplied token is canceled.
- Fall back only for provider, parsing, network, or data errors that are safe to degrade.
- Log unexpected failures where the current behavior would otherwise hide them.

The first target is `RetrievalService.TryEmbeddingScoreAsync`, where cancellation currently becomes lexical scoring. Apply the same rule consistently across the other provider fallback paths.

### 4. Make git process execution robust

Refactor `GitClient.RunAsync` so external git processes cannot deadlock or survive cancellation.

Required behavior:

- Read stdout and stderr concurrently.
- Preserve useful stderr in `GitCommandException`.
- Register cancellation to terminate the git process and its child process tree where supported.
- Await process exit during cleanup.
- Avoid leaking a running process when `ReadToEndAsync` or `WaitForExitAsync` is canceled.
- Keep argument passing through `ProcessStartInfo.ArgumentList`; do not reintroduce shell command construction.

Add tests around command failure, cancellation, and large stderr output where practical.

### 5. Publish provider-registry snapshots atomically

Change `ProviderRegistry` so a new provider dictionary is fully constructed before updating `_version` or publishing it.

If one provider configuration is invalid:

- Do not replace a valid existing registry with a partially built state.
- Prefer skipping the invalid provider and logging the configuration problem, or fail the refresh with a clear diagnostic while keeping the previous registry.
- Ensure the next settings version can retry construction rather than being permanently associated with a stale dictionary.

Review thread safety because `ProviderRegistry` is a singleton and provider properties may be read concurrently while settings refreshes occur.

### 6. Enforce one primary AI provider

Make `AiSettingsService.SaveAsync` and `SetPrimaryAsync` safe under concurrent requests.

Use a transaction or another serialization strategy while clearing existing primaries and setting the new primary. Add a database-level invariant where supported, such as a PostgreSQL filtered unique index for `IsPrimary = true`. `TesseraDbContext` already uses this pattern for `EdgeHistory` (`HasFilter("\"Live\" = true")`); follow the same convention for `AiSettings.IsPrimary`.

If a migration is added:

- Clean up existing duplicate primary rows deterministically before creating the constraint.
- Update the EF model snapshot.
- Add an integration test for the invariant.

### 7. Dispose external HTTP responses

Wrap non-streaming `HttpResponseMessage` instances in `using` declarations in:

- `GitHubAppClient`.
- `GitHubOAuthClient`.
- `OpenAiCompatibleChatProvider`.
- Any other adapter found during implementation with the same pattern.

Preserve response-body parsing and error details. Ensure disposal occurs on success, non-success responses, malformed JSON, and cancellation.

### 8. Keep timeout and retry behavior consistent

Review external provider operations for consistent timeout and cancellation behavior. Streaming calls should have an explicit request timeout equivalent to non-streaming calls, while still honoring caller cancellation.

Retry logic must not retry caller cancellation. It may retry provider timeouts, transient HTTP failures, and rate limits according to the existing bounded delay policy.

## Implementation Sequence

1. Add characterization tests for object-store traversal, cache cancellation, retrieval cancellation, provider refresh failure, and AI primary concurrency.
2. Harden `FileSystemObjectStore` path resolution and tests.
3. Fix `AiSettingsCache` semaphore ownership, cancellation, and refresh scheduling.
4. Fix cancellation propagation in retrieval and provider fallback paths.
5. Refactor git process output and cancellation handling.
6. Make `ProviderRegistry` construction/publishing atomic and resilient to invalid provider settings.
7. Serialize AI primary-provider updates and add a database invariant/migration if required.
8. Dispose HTTP responses and align external-call timeout behavior.
9. Run focused tests, then the complete solution test suite.

## Verification

Run from the repository root:

```powershell
dotnet test tests/Tessera.Integration.Tests --no-restore
dotnet build Tessera.slnx
dotnet test tests/Tessera.Domain.Tests --no-restore
```

Required test coverage:

- Object-store keys cannot write, read, or probe outside the configured root.
- Nested object-store keys continue to work.
- Cache cancellation before semaphore acquisition does not alter semaphore capacity.
- Concurrent cache refreshes do not run database loads in parallel.
- Canceled embedding retrieval propagates cancellation.
- Provider failures still fall back to lexical scoring or rule-based behavior as intended.
- Git cancellation terminates the process and does not leave a child process running.
- Large stdout and stderr output do not deadlock git execution.
- An invalid provider configuration does not permanently retain or publish a stale provider registry.
- Concurrent primary-provider updates leave exactly one primary.
- HTTP responses are disposed on all completion paths.
- Caller cancellation is not retried by provider retry policies.

## Compatibility Considerations

- Preserve existing `IObjectStore`, provider, git, and review service interfaces unless a cancellation or disposal fix requires a signature change.
- Preserve fallback behavior for ordinary provider failures.
- Preserve stored object key formats.
- If a database index is introduced, migrate existing duplicate-primary data before enforcing it.
- Avoid changing AI response formats or PR review output as part of this infrastructure hardening.

## Non-Goals

- Do not redesign the entire provider abstraction.
- Do not replace EF Core or change database providers.
- Do not change API endpoint authorization in this plan; that is covered by the API refactoring plan.
- Do not alter the Domain Merkle algorithm in this plan; that is covered by the Domain refactoring plan.

## Completion Criteria

- Filesystem operations are root-confined.
- Cache synchronization remains correct under cancellation and concurrent refreshes.
- Cancellation reaches external and fallback operations.
- Git processes are cleaned up reliably.
- Provider configuration refreshes cannot publish stale or partially built state.
- AI primary-provider uniqueness is enforced under concurrency.
- HTTP response resources are disposed consistently.
- Infrastructure integration tests and the full solution build pass.
