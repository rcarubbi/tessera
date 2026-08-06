## Context

Greenfield. Nenhum código existe ainda. Decisões de produto travadas com o cliente: multi-linguagem (tree-sitter), descoberta híbrida (AST estático + IA semântica), provedores LLM gratuitos (DeepSeek/Qwen/GLM), entrada via GitHub App. O diferencial do produto é o **Merkle DAG**: re-análise incremental (nunca reprocessar monólito inteiro), snapshots por commit (time-travel) e consultas estruturais confiáveis a custo ~zero, com IA só para semântica.

## Goals / Non-Goals

**Goals:**
- Pipeline incremental: mudou 5 arquivos de 2M linhas → IA processa 5 nodes, não o monólito.
- Ground truth estático (AST) separado de inferência de IA (confidence por edge/node).
- Snapshot imutável por commit, content-addressed, com root hash (Merkle DAG, não tree).
- Custo LLM ~zero no MVP: provedores free, tiering, orçamento por repo/dia.
- Auditabilidade: provenance (commit, modelo, promptVersion, timestamp) em todo node.

**Non-Goals:**
- Não é um analisador de segurança/vulnerabilidades.
- Não suporta diffs semânticos profundos de refactoring no MVP 1.
- Sem mercado de plugins/linguagens novas no MVP (gramáticas fixas: C#, Java, JS/TS, Python, Go, PHP, Ruby).
- Sem execução de código do repositório analisado — análise é read-only.
- Não roda LLM localmente no MVP (Ollama fica como opção futura de venda on-prem).

## Decisions

### D1. Dois hashes por node: `structuralHash` e `semanticHash`
- `structuralHash` = SHA-256 do AST normalizado (símbolo, tipo, assinatura, edges estáticos), sem comentários/whitespace. Determinístico, custo zero.
- `semanticHash` = SHA-256(markdown do node + hashes sorted dos children). Propaga em cascata.
- *Por quê*: hash de bytes do arquivo re-dispara IA em mudança de comentário. Este modelo só chama LLM quando a estrutura realmente mudou.
- *Alternativa considerada*: hash byte-a-byte do arquivo (rejeitada: reprocessa à toa).

### D2. Invalidação em cascata sem LLM
Regra: `structuralHash` mudou → IA reanalisa só esse node. `semanticHash` dos pais recalcula (hashing barato). Markdown do pai ganha flag `stale`; re-summarize só quando dependência direta mudou estruturalmente e política de profundidade permite.
- *Por quê*: mantém custo baixo e integridade do DAG.
- *Risco*: markdown de pai pode ficar defasado. Mitigado por flag `stale` + fila de revisão humana.

### D3. Híbrido: AST é ground truth, IA preenche lacunas
Edges estáticos (calls, inheritance, imports) têm confidence ~1.0 com `evidence: file:line`. IA só infere: papel do componente, bounded context, eventos implícitos, edges que AST não pega.
- *Por quê*: LLM free hallucina estrutura. AST não.
- *Alternativa considerada*: descoberta pura IA (rejeitada: cara, lenta, não auditável).

### D4. Resolução cross-file no MVP
Resolver via imports/namespaces + matching de símbolos; dentro do arquivo, matching por escopo. Edges cross-file ambíguos recebem confidence baixa e vão para revisão.
- *Risco*: sobrescrita/overload podem errar. Aceito no MVP; confidence baixa + fila de revisão mitigam.
- *Alternativa considerada*: índice de símbolos global completo (adiado — complexidade alta para MVP).

### D5. Sidecar Node.js para tree-sitter
Parsing roda em serviço HTTP separado (Node) porque o ecossistema de gramáticas tree-sitter em JS é superior ao de bindings .NET. Worker .NET orquestra e chama o sidecar.
- *Por quê*: multi-linguagem no MVP sem reescrever gramáticas.
- *Alternativa considerada*: Tree-sitter.NET via P/Invoke (rejeitada: menos gramáticas, mais risco de build nativo).

### D6. Storage content-addressed + índice PostgreSQL
Node imutável gravado no object store com nome = `semanticHash` (filesystem dev → S3-compatible prod). PostgreSQL guarda: `nodes`, `edges`, `snapshots`, provenance, fila de revisão, estado por repo.
- *Por quê*: idempotência gratuita (mesmo hash = no-op), time-travel barato, custo MVP ~zero.
- *Alternativa considerada*: Neo4j/SurrealDB desde o início (adiado: PostgreSQL resolve consultas do MVP com joins/btree; migra se escalar).

### D7. Merkle DAG, não tree
Arquitetura real não é hierárquica (serviço depende de vários e é dependido por vários). Root hash do snapshot = SHA-256 sobre o conjunto sorted de todos os `semanticHash`. Nenhum nó tem pai único.

### D8. Abstração de provider LLM com fallback
`IChatProvider` (DeepSeek/Qwen/GLM via API OpenAI-compatible), config por deploy (base URL, model, key). Retry com backoff + fallback para secundário. Tiering: modelo pequeno p/ entidade simples, maior p/ complexa. Orçamento de tokens por repo/dia pausa análise.

### D9. Snapshot por commit, consultas com versão
Todo snapshot é imutável e referenciado por commit SHA. Toda consulta estrutural mira um snapshot (default: último). Diff arquitetural = comparação de dois snapshots. Detecção de ciclo novo = algoritmos de ciclo no delta do grafo.

### D10. Pipeline como fila de jobs
Webhook push → enqueue job → worker processa: ingest (clone/fetch) → parse (sidecar) → diff de hashes → IA incremental → DAG/snapshot → index. Idempotente por commit SHA. Execução em container isolado com limites.

## Risks / Trade-offs

- **[Resolução cross-file imperfeita]** → Confidence baixa em edges ambíguos, fila de revisão humana, melhoria incremental.
- **[Qualidade dos LLM free]** → AST como ground truth (IA nunca decide estrutura), tiering, fallback de provider, confidence + revisão.
- **[Markdown de pai defasado após cascata]** → Flag `stale`, política de profundidade de re-summarize, revisão humana.
- **[Mudança de prompt invalida nodes antigos]** → `promptVersion` em todo node; regeneração opcional.
- **[Orçamento de tokens de provedor free]** → Limites por repo/dia, pausa automática, tiering por complexidade.
- **[Repo gigante / parse lento]** → Processamento por diff desde o primeiro snapshot (parse só do que mudou + árvore de arquivos nova).

## Migration Plan

- Produto greenfield: sem migração de dados. Rollback = desativar feature flag de ingesta/análise; nodes e snapshots são imutáveis e consultáveis via API a qualquer momento.
- Deploy: Docker Compose local; produção em containers (API, worker, sidecar, web, Postgres).

## Open Questions

- URL de ingressão no GitHub App: requer domínio público + HTTPS (túnel para dev).
- Threshold exato de confidence p/ fila de revisão (default 0.7, calibrar com seed de repos).
- Modelo de preço/planos (pós-MVP): por repo? por commits/mês? — fora do escopo técnico, decidir com produto.
- On-prem/self-host com Ollama como opção de venda para empresas que não mandam código pra nuvem (futuro).
