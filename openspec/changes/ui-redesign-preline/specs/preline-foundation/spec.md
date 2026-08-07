## ADDED Requirements

### Requirement: Preline UI installed and wired
The web app SHALL depend on `preline` (and `@tailwindcss/forms`), with the Tailwind v4 CSS variants configured in `globals.css` (`@source` for `preline/dist/*.js`, `@import` of `preline/variants.css`, and the forms `@plugin`).

#### Scenario: Build emits Preline utilities
- **WHEN** the web app is built
- **THEN** the generated CSS includes Preline utility/variant classes and the build succeeds

### Requirement: Client-side Preline lifecycle
Preline SHALL be initialized client-side: a `PrelineClient` component dynamic-imports `preline/non-auto` and runs `HSStaticMethods.autoInit()` on mount and after every route change (via `usePathname()`), with unmount cleanup, without touching the DOM during server rendering.

#### Scenario: Initial load initializes plugins
- **WHEN** a page loads
- **THEN** Preline scans the committed route markup and wires its plugins after hydration

#### Scenario: Route navigation rescans
- **WHEN** the user navigates within the App Router
- **THEN** Preline re-scans the new route's DOM (collection-aware: stale nodes dropped, initialized nodes skipped)

#### Scenario: No server-side DOM access
- **WHEN** the app is server-rendered
- **THEN** Preline is not imported or executed on the server

### Requirement: Legacy component CSS removed
The bespoke component classes in `globals.css` (`.card`, `.btn`, `.badge`, `.panel`, `.field`, `.spinner`, `.muted`, `.list`, etc.) SHALL be removed in favor of Preline/utility classes, while the `@theme` color tokens and base resets are retained.

#### Scenario: No bespoke component classes remain
- **WHEN** `globals.css` is reviewed
- **THEN** it contains no bespoke component classes; theme tokens and base resets remain
