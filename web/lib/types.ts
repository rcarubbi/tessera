export type Repository = {
  id: string;
  githubId: number;
  owner: string;
  name: string;
  fullName: string;
  defaultBranch: string;
  cloneUrl?: string | null;
  installationId: number;
  isConnected: boolean;
  status: number;
  lastProcessedCommit: string | null;
  lastSnapshotAt: string | null;
  nodeCount: number;
  edgeCount: number;
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
};

export type GraphEdge = {
  from: string;
  to: string;
  type: string;
  evidence?: string | null;
  confidence: number;
  isStatic: boolean;
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
export type Impact = { entity: string; commitSha: string; items: ImpactItem[] };

export type ConsumerItem = {
  fromKey: string;
  fromSymbol: string;
  path: string;
  line: number;
  type: string;
  evidence?: string | null;
  confidence: number;
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
};
export type Chain = { entity: string; commitSha: string; items: ChainItem[] };

export type AiProviderCatalogItem = { name: string; baseUrl: string; model: string };

export type AiSettings = {
  providerName: string;
  baseUrl: string;
  model: string;
  apiKeyMasked: string | null;
  hasApiKey: boolean;
  fallbackProviderName: string | null;
  updatedAt: string | null;
  availableProviders: AiProviderCatalogItem[];
};

export type AiSettingsRequest = {
  providerName: string;
  baseUrl: string;
  model: string;
  apiKey?: string | null;
  fallbackProviderName?: string | null;
};
