## Why

Sistemas legados de 10-20 anos perdem arquitetura: documentação desatualizada, nenhum desenvolvedor entende o todo, e mudanças viram apostas. Ferramentas atuais (IDE search, CodeRabbit, README gerado) respondem mal perguntas operacionais que surgem todo dia: "o que quebra se eu alterar esta classe?", "quem consome este evento?". O custo de re-descoberta é pago a cada onboarding e a cada refactor.

Tessera resolve isso: o usuário conecta um repositório Git e recebe um **knowledge graph versionado** do sistema — nodes de conhecimento em Markdown gerados por IA, amarrados por um **Merkle DAG** que permite processamento incremental e consultas estruturais confiáveis.

## What Changes

- Criar produto MVP **Tessera** (greenfield, monorepo).
- Conectar repositórios via **GitHub App** (OAuth + webhook de push) com clone incremental.
- Extrair estrutura do código **sem IA**: parser tree-sitter multi-linguagem gera entidades, dependências e edges estáticos com `structuralHash` (ground truth, custo zero).
- Gerar **knowledge nodes** em Markdown por entidade via agentes de IA, com `semanticHash`, confidence e provenance (commit, modelo, versão de prompt).
- Armazenar em **object store content-addressed** + índice PostgreSQL; **snapshot por commit** com root hash (Merkle DAG, não tree).
- Re-analisar de forma **incremental**: só nodes cujo `structuralHash` mudou; propagação em cascata barata via hash.
- Expor **query layer**: "o que quebra se eu mudar X?" (fecho transitivo), "quem consome este evento?" (reverse edges), diff arquitetural entre commits, export Mermaid.
- Expor **chat com RAG**: perguntas semânticas sobre a arquitetura, com citações `file:line`.
- Dashboard **Next.js**: lista de repositórios, grafo interativo, diff view, chat, fila de revisão de confidence baixa.
- Suportar **provedores LLM gratuitos/baratos** (DeepSeek, Qwen, GLM) com abstração de provider, fallback, tiering e orçamento de tokens.

## Capabilities

### New Capabilities
- `repo-ingestion`: Conectar e sincronizar repositórios Git via GitHub App (OAuth, webhook push, clone incremental).
- `static-analysis`: Extração determinística de estrutura de código multi-linguagem (entidades, edges, `structuralHash`) sem IA.
- `ai-semantics`: Geração de knowledge nodes Markdown por IA (agentes: summarizer, relationship, architect) com `semanticHash`, confidence e provenance.
- `merkle-dag-store`: Armazenamento content-addressed, snapshots por commit, invalidação incremental em cascata.
- `architecture-query`: Consultas de grafo estrutural (impacto, consumers, diff arquitetural, Mermaid).
- `rag-chat`: Chat sobre a arquitetura com recuperação por nodes e citações `file:line`.
- `web-dashboard`: Interface web (Next.js) para navegar, comparar, revisar e perguntar.

### Modified Capabilities
<!-- Nenhuma - produto greenfield, sem specs existentes em openspec/specs/. -->

## Impact

- **Novo monorepo** `tessera/` com: API ASP.NET Core, Worker de pipeline, sidecar Node.js (tree-sitter), frontend Next.js, Docker Compose.
- **Dependências externas**: GitHub App, tree-sitter, PostgreSQL, provedores LLM (DeepSeek/Qwen/GLM), object store (filesystem dev → S3-compatible prod).
- **Nenhum impacto em sistemas existentes** — produto novo, self-contained, análise executada em containers isolados.
- **Novas specs** em `openspec/specs/` para as 7 capabilities após archive.
