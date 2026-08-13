# Tessera.Domain Refactoring Plan

## Goal

Make snapshot hashing deterministic and complete for deep dependency graphs, ensure duplicate relationships cannot create inconsistent hashes, and validate parser input before it becomes persisted domain data.

## Scope

- `src/Tessera.Domain/Merkle/MerkleDag.cs`
- `src/Tessera.Domain/Merkle/SemanticHasher.cs`
- `src/Tessera.Domain/Merkle/SnapshotComposer.cs`
- `src/Tessera.Domain/Parsing/ParseResult.cs`
- `src/Tessera.Domain/Entities/ArchitectureRule.cs` (naming cleanup only if needed)
- `tests/Tessera.Domain.Tests/MerkleTests.cs`
- `tests/Tessera.Domain.Tests/SnapshotComposerTests.cs`
- New domain tests for parser validation if validation is introduced there

## Design Decisions

### 1. Replace fixed-iteration Merkle propagation

Remove the fixed `MaxIterations = 10` convergence limit from `MerkleDag.ComputeHashes`.

For acyclic graphs, compute hashes in dependency order so every changed child is reflected in all ancestors regardless of graph depth or input enumeration order. Keep explicit cycle handling so cyclic input terminates deterministically.

Preferred behavior:

- Leaf hashes are computed from content with no resolved children.
- A node is computed only after its known dependencies have hashes.
- Missing child keys are ignored consistently with the current behavior, or reported if validation makes missing references invalid.
- Cycles do not hang; they use a documented deterministic fallback or bounded cycle handling.
- Results are independent of the order of `DagNode` input.

Concrete approach: compute strongly connected components (e.g. Tarjan's algorithm) to find cycles, process the condensation graph in topological order, and within each multi-node SCC apply the existing fixed-point iteration only to the (typically small) cycle members. This bounds the fixed-point work to actual cycles instead of the whole graph and removes the depth-dependent iteration count for acyclic portions.

This decision depends on relationship normalization (decision 2): build the DAG from the normalized, deduplicated relationship set, not the raw parser output, otherwise duplicate edges can distort SCC detection.

Do not silently increase the iteration limit as the only fix; that preserves the depth and ordering defect.

### 2. Normalize relationships once in `SnapshotComposer`

Before creating Merkle nodes or graph edges, normalize parser relationships using `(From, To, Type)` as the identity.

Use the normalized relationship set for both:

- Child hash inputs.
- Persisted `GraphEdge` creation.

Define the behavior for duplicate metadata explicitly. The first relationship, highest-confidence relationship, or a deterministic merge should win. Preserve existing public edge behavior unless the tests establish a better rule.

This guarantees that duplicate parser output cannot change `RootHash` while leaving the persisted edge set unchanged.

### 3. Make hash serialization unambiguous

Replace delimiter-only concatenation in `SemanticHasher.Compute` with a structured, deterministic representation.

Options include:

- Serialize a small internal payload with sorted children and hash the resulting UTF-8 JSON.
- Use length-prefixed fields.

The representation must preserve:

- Content.
- Child key.
- Edge type.
- Child hash.
- Deterministic child ordering.

Rename `StableJson` if it remains. It currently returns a hash, not JSON; `HashStableJson` would describe its behavior more accurately. Keep this naming change separate from the hashing algorithm if backward compatibility of generated hashes matters.

### 4. Validate parser results at the domain boundary

Add a validation step for `ParseResult` before composition. It should detect at least:

- Duplicate entity keys.
- Empty entity or relationship keys.
- Negative or invalid line ranges.
- Confidence values outside the supported range.
- Relationships whose endpoints do not exist.

Choose one consistent contract:

- Throw a domain-specific validation exception; or
- Return a validation result containing errors.

Do not silently drop malformed relationships without making the failure visible. If missing endpoints are intentionally tolerated for parser compatibility, record diagnostics and test that behavior explicitly.

### 5. Preserve EF-friendly entities

Do not broadly convert persistence entities to private-setter aggregates in this change. The current entities are shared with EF Core and Infrastructure, and changing their construction model would expand the scope without directly fixing the identified domain bugs.

Only rename or clarify members when it has no persistence/API compatibility impact.

## Implementation Sequence

1. Add characterization tests proving that deep dependency chains and different input orders currently expose the Merkle defect.
2. Refactor `MerkleDag.ComputeHashes` to be depth-independent and order-independent, with deterministic cycle behavior.
3. Add structured hash serialization and update Merkle tests for delimiter-containing values.
4. Normalize relationships in `SnapshotComposer` and use the same normalized set for hashes and edges.
5. Add parser-result validation and decide whether invalid input throws or returns diagnostics.
6. Update `SnapshotComposer` tests for duplicate entities, duplicate relationships, missing endpoints, invalid line ranges, and invalid confidence.
7. Review `StableJson` naming and `ParentSemanticHash` usage; change only if the behavior and persistence contract are clear.
8. Run the domain tests, then the full solution tests to detect changes in generated snapshot hashes or downstream assumptions.

## Verification

Run from the repository root:

```powershell
dotnet test tests/Tessera.Domain.Tests --no-restore
dotnet build Tessera.slnx
dotnet test tests/Tessera.Integration.Tests --no-restore
```

Required test coverage:

- An 11+ level dependency chain propagates a leaf change to every ancestor.
- Hashes are identical when nodes are supplied in different orders.
- Cyclic graphs terminate and produce deterministic results.
- Duplicate relationships produce the same hashes as a single relationship.
- Duplicate relationships still produce one persisted graph edge.
- Content and child values containing delimiters cannot create equivalent serialized payloads accidentally.
- Missing child references follow the documented validation behavior.
- Duplicate entity keys are rejected or deterministically handled.
- Invalid line ranges and confidence values are rejected or normalized according to the chosen contract.
- Snapshot composition remains deterministic for equivalent parse results.

## Compatibility and Migration Considerations

Changing hash serialization or Merkle propagation can change `SemanticHash` and `RootHash` values for existing snapshots. Before implementation:

1. Confirm whether hashes are used only for comparison or exposed as durable identifiers.
2. If hashes are durable, version the hashing algorithm or provide a migration strategy.
3. Do not silently mix old and new algorithms in the same comparison path.
4. Add a version marker if consumers need to distinguish old and new hashes.
5. `GraphQueryService.DiffAsync` and rule-drift comparisons compare `SemanticHash` directly across snapshots; a snapshot computed before this change and one computed after will show spurious "changed" nodes even without a real code change. Plan a full (non-incremental) reprocess of active repositories after deploying the hashing changes, and communicate this to users of diff/drift features.

## Non-Goals

- Do not redesign the entire domain entity model.
- Do not change graph query semantics unrelated to hash calculation.
- Do not modify parser sidecar behavior unless required to satisfy the chosen validation contract.
- Do not change API response shapes as part of this Domain work.

## Completion Criteria

- Merkle hashes are independent of dependency depth and input enumeration order.
- Cycles terminate deterministically.
- Hash inputs are structured and unambiguous.
- Relationship normalization is shared by hashing and edge persistence.
- Invalid parser input is visible through a documented domain contract.
- Domain and integration tests pass, or any intentional hash compatibility impact is documented and migrated.
