## 1. Scaffold e infraestrutura

- [x] 1.1 Criar monorepo `tessera/`: projetos ASP.NET Core (Api, Worker, Domain, Infrastructure, Shared), frontend Next.js, sidecar Node.js, Docker Compose, `.gitignore`, README
- [x] 1.2 Subir Docker Compose de dev: Postgres + object store (filesystem) + API + Worker + sidecar + web
- [ ] 1.3 Configurar GitHub App (manifest, webhook, OAuth App) e secrets de dev — código pronto/testado; falta gerar credenciais reais
- [x] 1.4 Definir schema PostgreSQL inicial (EF Core migrations): `repositories`, `installs`, `nodes`, `edges`, `snapshots`, `review_items`

## 2. repo-ingestion

- [x] 2.1 Implementar fluxo OAuth do GitHub (autorizar App, listar repos acessíveis, persistir installation id)
- [x] 2.2 Implementar webhook `push` → validar instalação → enqueue job por (repo, commit sha)
- [x] 2.3 Implementar clone incremental: full clone no primeiro uso, `git fetch` + diff contra último commit processado
- [x] 2.4 Persistir estado por repo (último commit, branch, status do job) e garantir idempotência por commit SHA
- [x] 2.5 Executar clone/análise em container isolado com limites de tempo/memória/CPU, nunca executando código do repo
- [x] 2.6 Seed: 2-3 repos públicos pequenos (.NET + TypeScript) como fixtures de teste

## 3. static-analysis

- [x] 3.1 Criar sidecar Node.js com tree-sitter e gramáticas: C#, Java, JavaScript, TypeScript, Python, Go, PHP, Ruby
- [x] 3.2 Endpoint de parse: entrada (files, language) → saída JSON (entidades: símbolo, kind, path, range; edges: calls/reference/inheritance/import; imports)
- [x] 3.3 Resolver edges intra-arquivo por matching de símbolo no escopo
- [x] 3.4 Resolver edges cross-file via imports/namespaces + matching de símbolos, com confidence e reason
- [x] 3.5 Calcular `structuralHash` por entidade (AST normalizado, sem comentários/whitespace) e garantir determinismo
- [x] 3.6 Integrar sidecar ao Worker: parse de diff de arquivos, armazenar entidades/edges no Postgres
- [x] 3.7 Teste de regressão: parses repetidos do mesmo commit geram output idêntico

## 4. ai-semantics

- [x] 4.1 Implementar `IChatProvider` + providers DeepSeek, Qwen, GLM (API OpenAI-compatible), config por deploy
- [x] 4.2 Implementar retry com backoff e fallback para provider secundário
- [x] 4.3 Implementar tiering: modelo pequeno p/ entidades simples, maior p/ complexas (threshold por tamanho)
- [x] 4.4 Implementar orçamento de tokens por repo/dia com pausa automática
- [x] 4.5 Criar agente Summarizer: markdown knowledge node (type, responsibilities, dependencies, incoming/outgoing, events, confidence)
- [x] 4.6 Criar agente Relationship: edges que AST não pega (eventos implícitos, DI runtime) — via heurística no sidecar: `kind` real (interface/struct/enum/record), `fieldTypes`/`injectedTypes` (ctor params) → edges `Implements`, `FieldDependency` (conf 0.7/0.9) e `Injected` (conf 0.85/0.95), cross-file com confidence < 1; validado no e2e e ao vivo
- [x] 4.7 Criar agente Architect: bounded context e papel do componente — `RuleBasedArchitect` (regras, sem LLM): contexto = primeiro segmento de pasta significativo (skip src/app/services/...), papel = suffixo/kind (Controller, Service, Repository, Contract, DTO, EventPublisher, ...); seção `## Architecture` no knowledge node; também exigida no prompt LLM (PromptVersion 1.1.0); `RuleBasedSummarizer` bump 0.1.0 invalida stale
- [x] 4.8 Gravar provenance (commit, modelo, promptVersion, timestamp) e flag de `stale` por promptVersion
- [x] 4.9 Versionar prompts em `Ai/Prompts/` com versão semântica

## 5. merkle-dag-store

- [x] 5.1 Implementar `semanticHash` = SHA-256(markdown + children hashes sorted)
- [x] 5.2 Implementar object store content-addressed (filesystem dev, interface p/ S3): gravação imutável, hash duplicado = no-op
- [x] 5.3 Implementar snapshot por commit (commit sha, root hash sobre conjunto sorted de semanticHash, node/edge counts)
- [x] 5.4 Implementar invalidação em cascata: recomputar hash bottom-up, marcar `stale` por política de profundidade
- [x] 5.5 Pipeline incremental: re-análise de IA só para nodes com `structuralHash` mudado
- [x] 5.6 Job de verificação de consistência object store ↔ índice
- [x] 5.7 Teste: mudança de comentário não muda hashes; mudança de assinatura propaga até root

## 6. architecture-query

- [x] 6.1 Implementar fecho transitivo de dependentes ("o que quebra se eu mudar X?") com depth e path
- [x] 6.2 Implementar reverse edges ("quem consome este evento?") com evidence
- [x] 6.3 Implementar mapeamento endpoint → controller → services → repositórios com confidence por edge
- [x] 6.4 Implementar diff arquitetural entre snapshots: added/removed/changed nodes+edges
- [x] 6.5 Implementar detecção de ciclo novo no delta do grafo, reportando path e commit
- [x] 6.6 Implementar export Mermaid com filtros (subgraph por módulo, depth limit)
- [x] 6.7 Escopar todas as consultas por snapshot (default: último)

## 7. rag-chat

- [x] 7.1 Implementar embeddings + índice vetorial por repo/snapshot (top-k e similarity threshold)
- [x] 7.2 Implementar roteamento de pergunta: estrutural → grafo; semântica → RAG + LLM
- [x] 7.3 Formatar respostas com citações `file:line` e flags de confidence/needs-review
- [x] 7.4 Tratar caso "sem contexto suficiente" sem fabricar resposta
- [x] 7.5 Streaming de resposta no backend e suporte a histórico por conversa

## 8. web-dashboard

- [x] 8.1 Autenticação e scoping de acesso por usuário/repo — OAuth GitHub (login/callback/logout/me), sessões, `AccessContext` (admin via `Dashboard__ApiKey` ou `Auth__Admins`; não-admin via installations do usuário), scoping por `RepositoryAccess`/`GuardRepoAsync` (401/403/404) em listagem, snapshots, queries, chat e review; web: botão "Sign in with GitHub", `?token=` pós-redirect, usuário no TopBar; migration `AddAuthSessionsAndUsers`; testes (9) — falta gerar credenciais OAuth reais (com `1.3`)
- [x] 8.2 Tela de repos conectados: status, último snapshot, node count
- [x] 8.3 Grafo interativo (foco em entidade, expandir vizinhos, filtros por módulo/tipo de edge, seletor de snapshot)
- [x] 8.4 Tela de diff entre dois commits
- [x] 8.5 Painel de detalhe de entidade (knowledge node + provenance + consumers)
- [x] 8.6 Fila de revisão humana: aceitar/editar/dismiss, novo version com provenance preservada
- [x] 8.7 Painel de chat com citações clicáveis

## 9. Hardening e QA

- [x] 9.1 Integração contínua: build, testes de pipeline, testes de regressão de hash
- [x] 9.2 Testes e2e com repos seed (ex: "o que quebra" + diff entre commits)
- [x] 9.3 Documentação de deploy (Docker Compose, envs, configuração de providers)
- [x] 9.4 Validação de segurança: secrets, webhook signature, sandbox de análise
- [x] 9.5 Benchmark de custo LLM com repos reais e calibração do threshold de confidence
