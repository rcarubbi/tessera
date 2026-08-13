# Tessera Web Refactoring Plan

## Goal

Harden the dashboard’s client-side security, make asynchronous data flows race-safe, preserve chat history ordering, and improve accessibility and navigation behavior without changing the core dashboard workflows.

## Scope

- `web/lib/api.ts`
- `web/components/AuthContext.tsx`
- `web/components/Mermaid.tsx`
- `web/components/DiagramViewer.tsx`
- `web/components/ChatPanel.tsx`
- `web/components/ReviewPanel.tsx`
- `web/components/RepoHub.tsx`
- `web/components/GraphView.tsx`
- `web/app/settings/page.tsx`
- `web/app/repos/page.tsx`
- `web/app/layout.tsx`
- Related API/auth endpoint changes if cookie authentication or atomic chat persistence is introduced
- Web tests and browser smoke tests

## Design Decisions

### 1. Replace browser-accessible bearer-token storage

Remove long-lived bearer tokens from `localStorage`.

Preferred approach:

- Use an HttpOnly, Secure, SameSite cookie issued by the API.
- Keep session revocation server-side.
- Remove token query-string handoff from the OAuth flow where possible; use a one-time exchange code or establish the cookie at the callback.
- Update `AuthContext` and `lib/api.ts` so requests rely on credentials/cookies rather than manually constructed bearer headers.
- Preserve logout and session-expiration behavior.

If a short-term bearer-token migration is required, document the residual XSS risk and minimize token lifetime while the cookie flow is implemented.

This deployment currently serves the web app and API from different origins (`http://localhost:3000` and `http://localhost:5080` in `docker-compose.yml`). A cross-origin cookie requires `SameSite=None; Secure`, which in turn requires HTTPS even in local development, or a reverse proxy that serves both under one origin. Treat the origin/proxy decision as a prerequisite spike before implementing the cookie itself, and coordinate it with the API plan's `AuthEndpoints.cs` changes. If same-origin deployment is not feasible soon, keep the bearer-token approach but move it out of `localStorage` into memory only (re-authenticate on full page reload) as an interim step, and document that trade-off.

### 2. Harden Mermaid rendering

Mermaid diagrams can contain AI-generated, repository-derived, and edited content. Do not render that content with loose Mermaid security and raw HTML insertion.

Update both `Mermaid.tsx` and `DiagramViewer.tsx` to:

- Use Mermaid’s strict security mode.
- Sanitize generated SVG before insertion as an additional defense-in-depth measure.
- Reject or neutralize unsafe links, event handlers, embedded HTML, and script-like content.
- Keep diagram zoom and pan behavior unchanged.
- Add XSS regression tests using malicious labels, links, and HTML.

Prefer a trusted rendering wrapper that owns sanitization instead of duplicating `innerHTML` handling in two components.

### 3. Make chat persistence ordered and durable

The current `ChatPanel` posts the user and assistant messages concurrently after streaming completes. Persist a turn in order:

1. Persist the user message.
2. Persist the assistant message.
3. Update the UI only after the persistence result is known, or mark the local turn with a retryable persistence error.

Prefer an API operation that persists a complete chat turn atomically if the backend can support it. Otherwise await the user request before sending the assistant request and include a turn identifier for idempotency.

Handle failures visibly instead of silently swallowing both persistence errors. Do not lose a completed answer merely because saving history failed.

### 4. Cancel stale requests and prevent state races

Add `AbortController` or request-generation guards to effects that load repository, snapshot, graph, review, overview, rule, and file data.

At minimum:

- Abort the previous request when `repoId`, `commit`, or relevant filters change.
- Ignore late responses from obsolete requests.
- Do not set loading/error state after a component unmounts.
- Preserve the current data while a replacement request is loading where that produces a better UI.
- Treat `AbortError` as a normal cancellation, not a user-visible failure.

Apply the pattern consistently rather than fixing only `GraphView`.

### 5. Add destructive-action confirmation

Require confirmation or provide undo behavior for destructive actions, especially deleting an AI provider in [settings/page.tsx](web/app/settings/page.tsx).

The confirmation should identify the provider and explain the consequence. Disable the action while the request is pending and show a recoverable error if it fails.

Review other state-changing controls for the same requirement, including dismissing review findings where appropriate.

### 6. Restore semantic interactive controls

Replace clickable `<strong>` and `<div>` elements with semantic buttons or links:

- Review item symbol selection.
- Mermaid diagram opening.
- Modal backdrop close behavior where appropriate.

Ensure:

- Keyboard activation works.
- `type="button"` is explicit.
- Focus-visible styling is present.
- Icon-only controls have `aria-label`.
- Decorative icons use `aria-hidden="true"`.

### 7. Implement accessible modal behavior

Give `DiagramViewer` dialog semantics:

- `role="dialog"`.
- `aria-modal="true"`.
- An accessible dialog label.
- Focus moved into the dialog on open.
- Focus restored to the triggering control on close.
- Escape closes the dialog.
- Focus is trapped while open.
- `overscroll-behavior: contain` prevents background scrolling.

Keep the existing body-scroll lock and ensure cleanup is reliable.

### 8. Synchronize dashboard state with the URL

Persist state that users may want to bookmark or share:

- Active repository tab.
- Selected snapshot/commit.
- Selected graph entity where practical.
- Diff endpoints when the diff tab is open.

Use query parameters with validated defaults. Browser back/forward navigation should update the UI without creating inconsistent local state.

### 9. Improve async feedback and content semantics

Review loading, success, validation, and error surfaces:

- Add `aria-live="polite"` to asynchronous status messages.
- Keep error messages actionable and avoid exposing raw server exception bodies directly.
- Add labels, `name`, `autocomplete`, and correct input types to forms.
- Add an accessible label to search and filter controls.
- Add a skip link and ensure heading hierarchy is valid.
- Respect `prefers-reduced-motion` for diagram and UI animations.
- Use locale-aware date formatting consistently with `Intl.DateTimeFormat`.

## Implementation Sequence

1. Add security regression tests for token exposure and Mermaid content.
2. Resolve the same-origin/proxy question for cookie-based sessions (spike), then implement the cookie/session handoff, coordinating with the API callback and logout endpoints. This item can proceed in parallel with steps 3-6 below since it touches different files.
3. Centralize safe Mermaid rendering and add sanitization.
4. Make chat-turn persistence ordered/idempotent and surface persistence failures.
5. Add a shared abortable-fetch/effect pattern and apply it to repository-scoped data loaders.
6. Add provider-delete confirmation and review other destructive actions.
7. Replace non-semantic interactive elements and implement accessible dialog behavior.
8. Move repository tab/snapshot/diff state into validated URL parameters.
9. Complete async feedback, form metadata, motion, and locale-formatting cleanup.
10. Run typecheck, production build, focused tests, and browser smoke tests.

## Verification

Run from the web directory:

```powershell
npm run typecheck
npm run build
```

Required test or browser coverage:

- XSS payloads in Mermaid charts do not execute or produce unsafe SVG.
- Tokens are not stored in `localStorage` or exposed in URLs.
- Chat user/assistant history reloads in the correct order.
- Chat persistence failures are visible and retryable.
- Rapid commit changes cannot display stale graph data.
- Unmounting a page does not produce state-update warnings or visible stale errors.
- Provider deletion requires confirmation.
- Review and diagram interactions work with keyboard only.
- Diagram dialog traps/restores focus and closes with Escape.
- Tabs and snapshots survive refresh and support deep links/back-forward navigation.
- Async status messages are announced accessibly.
- Reduced-motion preferences disable or reduce nonessential animation.

## Compatibility Considerations

- Coordinate cookie authentication with the API and OAuth callback implementation.
- Preserve current dashboard routes and API response shapes where possible.
- If chat-turn persistence adds an endpoint or identifier, update shared API types and integration tests.
- Keep Mermaid diagrams and graph interactions visually consistent while changing their security boundary.
- Avoid introducing a state-management library unless the URL/abortable-fetch patterns cannot remain local and simple.

## Non-Goals

- Do not redesign the dashboard visual language.
- Do not change graph query or analysis semantics.
- Do not change provider or repository authorization rules in the web client; the API remains authoritative.
- Do not add client-side trust decisions for server-provided permissions.

## Completion Criteria

- Authentication material is not accessible to arbitrary page scripts.
- Mermaid content is rendered through a strict, sanitized path.
- Chat history is persisted in order and failures are visible.
- Stale asynchronous responses cannot overwrite current state.
- Destructive actions are confirmed or undoable.
- Interactive controls and dialogs are keyboard and screen-reader accessible.
- Stateful dashboard views are deep-linkable.
- `npm run typecheck` and `npm run build` pass, with browser smoke coverage for the affected workflows.
