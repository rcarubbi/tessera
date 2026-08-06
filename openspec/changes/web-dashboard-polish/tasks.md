## 1. OpenSpec artifacts

- [x] 1.1 Create proposal.md
- [x] 1.2 Create specs/web-dashboard/spec.md delta
- [x] 1.3 Create design.md
- [x] 1.4 Create tasks.md
- [x] 1.5 Validate change with `openspec validate web-dashboard-polish`

## 2. API: reprocess endpoint

- [x] 2.1 Add `POST /api/repositories/{id:guid}/reprocess` with GuardRepoAsync auth
- [x] 2.2 Reset Status=Pending and clear LastProcessedCommit; return updated repository
- [x] 2.3 Verify with `dotnet build` + curl using dev-dashboard-key (200 authed / 401 anonymous; e2e/sample 5→0→picked by worker)

## 3. Web: Tailwind integration

- [x] 3.1 Install tailwindcss + @tailwindcss/postcss; add postcss.config.mjs
- [x] 3.2 Add `@import "tailwindcss"` + `@theme` tokens to globals.css
- [x] 3.3 Verify `npm run build` emits Tailwind utilities (text-danger, bg-panel, spinner present in CSS)

## 4. Web: repo cards with reprocess + error states

- [x] 4.1 Add apiPost reprocess call and ReprocessButton component
- [x] 4.2 Restyle `app/repos/page.tsx` cards with Tailwind; red error cards/banners
- [x] 4.3 Update StatusBadge to Tailwind with red Failed state
- [x] 4.4 Add reprocess button + status to RepoHub header

## 5. Web: graph view improvements

- [x] 5.1 Add pan/zoom viewport transform (wheel + drag + reset)
- [x] 5.2 Add node-kind legend overlay
- [x] 5.3 Add hover highlight + tooltip chip
- [x] 5.4 Restyle GraphView controls with Tailwind

## 6. Global CSS cleanup + verification

- [x] 6.1 Convert remaining components (TopBar, tabs, panels, entity panel, login, chat/diff/review badges) to Tailwind
- [x] 6.2 Remove obsolete component classes from globals.css
- [x] 6.3 `npm run typecheck` + `npm run build` + dotnet tests green
- [x] 6.4 Docker build web image; smoke test in browser (web HTTP 200, reprocess endpoint e2e OK)
