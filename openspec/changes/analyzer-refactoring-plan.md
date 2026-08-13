# Tessera Analyzer Refactoring Plan

## Goal

Harden the analyzer sidecar against oversized or unauthorized requests, make Tree-sitter lifecycle and concurrency deterministic, preserve file-level diagnostics, and correct cross-file relationship resolution.

## Scope

- `analyzers/src/index.js`
- `analyzers/src/analyzer.js`
- `analyzers/test/analyzer.test.js`
- `analyzers/test/batch.test.js`
- `analyzers/package.json`
- `analyzers/Dockerfile`
- `docker-compose.yml`
- `src/Tessera.Infrastructure/Analysis/ParserSidecarClient.cs` if response diagnostics are exposed

## Design Decisions

### 1. Protect the parse endpoint

The analyzer is currently published on host port `4350` and accepts unauthenticated requests with an unbounded request body.

Add request protection:

- Remove or restrict the `"4350:4350"` port mapping in `docker-compose.yml`; the `worker` service already reaches the analyzer via the internal Docker network at `http://analyzers:4350`, so the host port publish appears to exist only for local debugging. If local access is still wanted, bind it to loopback only, e.g. `"127.0.0.1:4350:4350"`.
- Add a maximum request-body size.
- Reject oversized requests while receiving the body, before allocating more memory.
- Add a request timeout or connection timeout for slow clients.
- Validate that `files` is an array.
- Enforce maximum file count, path length, and source-content size.
- Return a controlled `413` response for oversized payloads and `400` for malformed payload structure.
- Keep CORS disabled or restricted unless browser clients genuinely need direct access.
- Add authentication if the analyzer must be reachable outside the trusted API/worker network.

Do not rely only on `NODE_OPTIONS=--max-old-space-size` as request protection.

### 2. Initialize Tree-sitter before accepting requests

Move `Parser.init()` before `server.listen()` so the process cannot advertise a listening port before its parser runtime is ready.

Startup should fail clearly if Tree-sitter initialization fails. The process should not accept parse requests in a partially initialized state.

Add a startup test or an extracted `startServer` function that verifies initialization occurs before listening.

### 3. Verify parser concurrency assumptions, then fix the real race

Node.js runs this process single-threaded with no `worker_threads` in use, and `parser.parse()` executes synchronously with no internal `await`. Because of this, two requests cannot literally interleave in the middle of the same `parse()` call the way they could with a truly parallel runtime — re-verify this against the `web-tree-sitter` version in use before treating full parser-instance isolation as urgent.

The real, verifiable race is in `loadParser`'s cold start: two concurrent requests for a language that is not yet cached can both pass the `parserCache.has(language)` check, both `await Parser.Language.load(wasm)`, and both construct/cache a `Parser`. This wastes work (the grammar loads twice, the second `parserCache.set` wins) but does not corrupt any single request's result, since each caller still parses with its own valid `Parser` instance.

Fix the cold-start race by caching the in-flight loading `Promise` (not just the resolved parser) so concurrent callers await the same load instead of starting duplicate loads:

- Cache a `Promise<Parser>` per language in `parserCache` immediately when loading starts.
- Concurrent callers await the same promise instead of re-invoking `Parser.Language.load`.

Only invest in per-operation `Parser` instances or a per-language mutex if the analyzer is later moved onto `worker_threads` or an equivalent truly parallel execution model, since that is the scenario where shared mutable parser state would become a genuine correctness risk.

Add a test that fires many concurrent first-time analyses for the same not-yet-cached language and asserts the grammar is loaded once and all results are correct.

### 4. Preserve file-level parse diagnostics

`analyzeBatch` records file errors but `handleParse` currently drops them and returns a successful-looking partial result.

Expose structured diagnostics in the response, for example:

```json
{
  "errors": [
    { "path": "src/Broken.cs", "message": "..." }
  ]
}
```

Update `ParserSidecarClient` and the Domain parse contract to either:

- Preserve diagnostics while allowing partial results; or
- Treat any file error as a batch failure.

Choose the behavior explicitly. The worker must not persist an incomplete graph as fully successful without knowing that files failed.

Add tests for one successful file plus one failing file, and for a completely failed batch.

### 5. Correct relationship suppression logic

Cross-file relationship checks are currently too broad. A source entity with any in-file relationship of a given type can suppress all cross-file relationships of that type.

Update checks to use the specific relationship identity:

- `from`.
- `to`.
- `type`.

Apply this to:

- Inheritance and implementation relationships.
- Field dependencies.
- Injected dependencies.

The analyzer should preserve existing same-file relationships while still resolving unique external targets.

Add tests where an entity has both local and cross-file relationships of the same type.

### 6. Make symbol resolution deterministic

Review ambiguous symbol handling:

- Avoid `Map` construction where duplicate fully qualified symbols silently overwrite earlier entities.
- Keep all candidates for ambiguous simple names.
- Emit a cross-file relationship only when the target is uniquely identifiable.
- Make candidate ordering deterministic.
- Ensure duplicate input paths or duplicate declarations do not produce unstable output.

If ambiguity is intentionally dropped, expose it as a diagnostic rather than silently omitting it.

## Implementation Sequence

1. Add tests for oversized requests, invalid payloads, startup ordering, concurrent parsing, partial file errors, and mixed local/cross-file relationships.
2. Extract request-body reading and payload validation helpers with explicit limits.
3. Initialize Tree-sitter before listening and define startup failure behavior.
4. Verify parser reentrancy assumptions and fix the `loadParser` cold-start race (or the full per-operation isolation if `worker_threads` are planned).
5. Preserve file-level diagnostics through `handleParse` and the C# sidecar client.
6. Fix relationship suppression to compare specific relationship targets.
7. Make symbol ambiguity and output ordering deterministic.
8. Update Docker Compose exposure and analyzer configuration.
9. Run analyzer tests and the integration tests that exercise the parser sidecar.

## Verification

Run from the analyzer directory:

```powershell
npm test
```

Run from the repository root:

```powershell
dotnet test tests/Tessera.Integration.Tests --no-restore
dotnet build Tessera.slnx
```

Required test coverage:

- Oversized request bodies return `413` without unbounded allocation.
- Invalid JSON and invalid `files` shapes return controlled `400` responses.
- Parser initialization completes before the server accepts requests.
- Concurrent same-language parsing produces deterministic results.
- Concurrent different-language parsing remains functional.
- File-level analyzer errors are returned to the caller.
- A partial parse cannot be mistaken for a clean successful parse.
- Local and cross-file field dependencies are both emitted.
- Local and cross-file injected dependencies are both emitted.
- Local and cross-file inheritance/implementation relationships are both emitted.
- Ambiguous symbols do not resolve to an arbitrary last-writer target.
- Relationship output remains deduplicated and deterministic.

## Compatibility Considerations

- Preserve existing entity and relationship field names.
- Preserve supported language and extension mappings.
- Keep partial results only if diagnostics are returned and downstream consumers handle them explicitly.
- Coordinate any response-contract change with `ParserSidecarClient` and Domain parsing models.
- Do not change confidence semantics unless required to distinguish ambiguous results.

## Non-Goals

- Do not redesign the Tree-sitter grammars.
- Do not add cross-technology AI linking; that remains an Infrastructure concern.
- Do not change Domain Merkle hashing in this plan.
- Do not expose the analyzer as a general public service.

## Completion Criteria

- The parse endpoint is bounded and reachable only through an intended trust boundary.
- Tree-sitter is initialized before requests are accepted.
- Parser instances are safe under concurrent requests.
- File failures are visible to downstream consumers.
- Cross-file relationship resolution no longer suppresses valid dependencies.
- Ambiguous symbol resolution is deterministic and does not silently select an arbitrary target.
- Analyzer and integration tests pass.
