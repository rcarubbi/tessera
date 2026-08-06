## ADDED Requirements

### Requirement: Multi-language source parsing
The system SHALL parse source code using tree-sitter grammars for at least: C#, Java, JavaScript, TypeScript, Python, Go, PHP, and Ruby. Unsupported files MUST be skipped with a recorded reason.

#### Scenario: Parse a C# repository
- **WHEN** an analysis job processes a repository containing C# source files
- **THEN** the system extracts entities and relationships for each supported C# file

#### Scenario: Unsupported file type
- **WHEN** the parser encounters a file with no registered grammar
- **THEN** the system records the file as skipped and continues

### Requirement: Entity extraction
The system SHALL extract typed entities: classes, interfaces, structs, enums, methods, functions, records, and modules. Each entity MUST carry its language, symbol name, kind, file path, and source range.

#### Scenario: Extract class with methods
- **WHEN** a file contains a class declaration with methods
- **THEN** the system produces one entity for the class and one for each method, linked parent-child

### Requirement: Static relationship extraction
The system SHALL extract static edges from the AST: calls, references, inheritance, implementation, imports, and field dependencies. Edges inside the same file MUST be resolved by symbol matching within the file scope.

#### Scenario: Direct call within same file
- **WHEN** a method in a file calls another method defined in the same file
- **THEN** the system emits a `calls` edge between the two entities with the call site line

#### Scenario: Cross-file reference
- **WHEN** a file imports a symbol from another file
- **THEN** the system attempts cross-file resolution via import/namespace matching and emits the edge with confidence reflecting resolution certainty

### Requirement: Structural hash
The system SHALL compute a deterministic `structuralHash` per entity from its normalized AST (symbol name, kind, signature, and static edges — excluding comments and whitespace). Equal structure MUST yield equal hash; comment-only changes MUST NOT change the hash.

#### Scenario: Comment-only change
- **WHEN** a file changes only in comments or whitespace
- **THEN** the `structuralHash` of all entities in the file is unchanged

#### Scenario: Signature change
- **WHEN** a method signature or its called symbols change
- **THEN** the `structuralHash` of the affected entity changes

### Requirement: No-LLM guarantee
The static analysis phase SHALL complete without calling any LLM provider. All outputs of this phase MUST be deterministic.

#### Scenario: Deterministic output
- **WHEN** the same commit is parsed twice
- **THEN** the extracted entities, edges, and hashes are identical
