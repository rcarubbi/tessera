export type Repository = {
  id: string;
  githubId: number;
  owner: string;
  name: string;
  fullName: string;
  defaultBranch: string;
  cloneUrl?: string | null;
  installationId: number;
  createdBy?: string | null;
  isConnected: boolean;
  status: number;
  lastProcessedCommit: string | null;
  lastSnapshotAt: string | null;
  nodeCount: number;
  edgeCount: number;
  stageStartedAt: string | null;
  processedCount: number;
  totalCount: number;
  errorMessage: string | null;
  cancelRequested: boolean;
  enablePrComments: boolean;
  analysisStartedAt: string | null;
  completedAt: string | null;
  createdAt: string;
  updatedAt: string;
};

export type Snapshot = {
  id: string;
  repositoryId: string;
  commitSha: string;
  rootHash: string;
  nodeCount: number;
  edgeCount: number;
  parentCommitSha: string | null;
  status: number;
  createdAt: string;
};

export type GraphNode = {
  key: string;
  symbol: string;
  path: string;
  kind: string;
  language: string;
  line: number;
  endLine: number;
  confidence: number;
  reviewStatus: string;
  semanticHash: string;
  content?: string | null;
  classDiagram?: string | null;
  sequenceDiagram?: string | null;
  classification?: string;
  factSource?: string;
  tier?: string;
  commitSha?: string;
  model?: string | null;
  promptVersion?: string | null;
  analyzedAt?: string;
  isTest?: boolean;
};

export type GraphEdge = {
  from: string;
  to: string;
  type: string;
  evidence?: string | null;
  confidence: number;
  isStatic: boolean;
  classification?: string;
  factSource?: string;
  tier?: string;
};

export type Graph = {
  commitSha: string;
  nodes: GraphNode[];
  edges: GraphEdge[];
};

export type DiffNode = { change: string; key: string; symbol: string; summary?: string | null };
export type DiffEdge = { change: string; from: string; to: string; type: string };
export type DiffCycle = { path: string[] };
export type Diff = {
  fromCommit: string;
  toCommit: string;
  nodes: DiffNode[];
  edges: DiffEdge[];
  cycles: DiffCycle[];
};

export type ReviewItem = {
  nodeId: string;
  key: string;
  symbol: string;
  path: string;
  line: number;
  endLine: number;
  kind: string;
  confidence: number;
  reviewStatus: string;
  model?: string | null;
  promptVersion?: string | null;
  content: string;
  editedAt?: string | null;
  editedBy?: string | null;
};

export type ReviewList = {
  commitSha: string;
  items: ReviewItem[];
};

export type ImpactItem = {
  key: string;
  symbol: string;
  path: string;
  line: number;
  depth: number;
  severity: string;
  trace: string[];
};
export type ImpactClassification = "test" | "api-contract" | "database-entity" | "other";
export type ImpactRating = "LOW" | "MEDIUM" | "HIGH" | "CRITICAL";
export type ImpactByType = { tests: number; apiContracts: number; databaseEntities: number; other: number };
export type ClassifiedImpactItem = ImpactItem & { classification: ImpactClassification; reason: string };
export type ImpactReport = {
  entity: string;
  commitSha: string;
  totalCount: number;
  directCount: number;
  indirectCount: number;
  maxDepth: number;
  byType: ImpactByType;
  rating: ImpactRating;
  items: ClassifiedImpactItem[];
};

export type ConsumerItem = {
  fromKey: string;
  fromSymbol: string;
  path: string;
  line: number;
  type: string;
  evidence?: string | null;
  confidence: number;
  classification?: string;
  factSource?: string;
  tier?: string;
};
export type Consumers = { entity: string; commitSha: string; items: ConsumerItem[] };

export type ChainItem = {
  key: string;
  symbol: string;
  path: string;
  line: number;
  depth: number;
  type: string;
  evidence?: string | null;
  confidence: number;
  classification?: string;
  factSource?: string;
  tier?: string;
};
export type Chain = { entity: string; commitSha: string; items: ChainItem[] };

export type EdgeHistoryEntry = {  type: string;
  introducedCommit: string;
  introducedAt: string;
  ageInDays: number;
};
export type EdgeHistory = {
  from: string;
  to: string;
  exists: boolean;
  commitSha: string;
  entries: EdgeHistoryEntry[];
};

export type ExplainedComponent = {
  key: string;
  symbol: string;
  path: string;
  line: number;
  kind: string;
  role: string;
};

export type CriticalComponent = {
  key: string;
  symbol: string;
  path: string;
  line: number;
  centrality: number;
};

export type ExplainResult = {
  hasSnapshot: boolean;
  emptyReason?: string | null;
  commitSha?: string | null;
  summary?: string | null;
  diagrams?: string[];
  rawOverview?: string | null;
  model: string;
  nodeCount: number;
  generatedAt: string;
  mainComponents: ExplainedComponent[];
  architecturalNotes: string[];
  externalSystems: string[];
  criticalComponents: CriticalComponent[];
};

export type RuleSeverity = "info" | "warning" | "error";
export type NodeSelector = {
  path?: string | null;
  kind?: string | null;
};
export type RuleConstraint = {
  kind: "deny" | "require";
  from: NodeSelector;
  to?: NodeSelector | null;
};
export type ArchitectureRule = {
  name: string;
  severity: RuleSeverity;
  constraint: RuleConstraint;
};
export type RuleSet = {
  yaml: string;
  rules: ArchitectureRule[];
};
export type RuleViolation = {
  ruleName: string;
  severity: RuleSeverity;
  fromKey: string;
  toKey: string;
  fromPath: string;
  fromLine: number;
  toPath: string;
  toLine: number;
  edgeType?: string | null;
  confidence: number;
  lowConfidence: boolean;
  isMissingRequirement: boolean;
};
export type RuleEvaluation = {
  commitSha: string;
  violations: RuleViolation[];
};
export type RuleDriftEntry = {
  ruleName: string;
  severity: RuleSeverity;
  fromKey: string;
  toKey: string;
  fromPath: string;
  toPath: string;
  edgeType?: string | null;
  introducedCommit: string;
  isLive: boolean;
  lowConfidence: boolean;
};
export type RuleDrift = {
  fromCommit: string;
  toCommit: string;
  entries: RuleDriftEntry[];
};

export type PrReviewStatus = "Queued" | "Reviewed" | "Posted" | "Failed";
export type PrReview = {
  id: string;
  prNumber: number;
  headSha: string;
  baseSha: string;
  status: PrReviewStatus;
  commentId?: number | null;
  commentBody?: string | null;
  errorMessage?: string | null;
  createdAt: string;
  updatedAt: string;
};
export type PrReviewList = { items: PrReview[] };

export type AiSettings = {
  providerName: string;
  baseUrl: string;
  model: string;
  apiKeyMasked: string | null;
  hasApiKey: boolean;
  endpoint: string | null;
  embeddingModel: string | null;
  embeddingEndpoint: string | null;
  isPrimary: boolean;
  updatedAt: string | null;
};

export type AiSettingsList = {
  providers: AiSettings[];
};

export type AiSettingsRequest = {
  providerName: string;
  baseUrl: string;
  model: string;
  apiKey?: string | null;
  endpoint?: string | null;
  embeddingModel?: string | null;
  embeddingEndpoint?: string | null;
  isPrimary?: boolean;
};
