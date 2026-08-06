## ADDED Requirements

### Requirement: Architecture chat
The system SHALL expose a chat interface where users ask questions about the architecture of a repository. The system SHALL decide per question whether to answer from the graph (structural) or from LLM + retrieved nodes (semantic).

#### Scenario: Structural question
- **WHEN** a user asks "what breaks if I change PaymentService?"
- **THEN** the system answers from the graph without an LLM call

#### Scenario: Semantic question
- **WHEN** a user asks "why does this event exist?"
- **THEN** the system retrieves relevant knowledge nodes and answers with an LLM

### Requirement: Citation of evidence
Every answer that relies on retrieved nodes MUST cite the source file and line. Answers from the graph MUST cite the edge evidence.

#### Scenario: Answer with file citation
- **WHEN** the chat answers using a knowledge node or graph edge
- **THEN** the answer includes `file:line` citations for each referenced entity

### Requirement: Node-scoped retrieval
The system SHALL retrieve nodes for chat using a RAG pipeline scoped to the repository and snapshot, with a configurable top-k and similarity threshold.

#### Scenario: Retrieval scoped to repository
- **WHEN** a user chats about repository A
- **THEN** the system never retrieves nodes from repository B

#### Scenario: Insufficient context
- **WHEN** no retrieved node exceeds the similarity threshold
- **THEN** the system answers "I couldn't find relevant context" and does not fabricate an answer

### Requirement: Confidence-aware answers
Chat answers SHALL reflect node confidence: low-confidence or "needs review" nodes MUST be flagged in the answer.

#### Scenario: Low-confidence node in answer
- **WHEN** a chat answer depends on a node flagged "needs review"
- **THEN** the answer includes a warning referencing the review queue
