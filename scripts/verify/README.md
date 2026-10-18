# Local UI verification

Run from `src/frontend` with Node 24+, .NET 10 and the repository's installed Playwright Chromium:

```sh
npm run verify-ui
npm run verify-ui -- --only /pharmacy --only /diagnostics --viewport mobile
npm run verify-ui -- --skip-build
```

Every run incrementally builds the backend solution and then the LiveCanary project in Release, including when the executable already exists, so the host uses the current API code. `--skip-build` explicitly skips both backend build commands; the frontend always builds with the isolated API URL. The runner fails clearly if the executable is missing with `--skip-build`.

The synthetic online host uses port 5328; Vite preview uses 5428. Occupied ports cause failure without stopping their owners. Each role logs in once and reuses one browser context across its routes and both viewports. Public pages use an anonymous context. `/notifications` uses receptionist; `/lab` and `/lab/tat` are explicitly skipped because the host has no LabSupervisor actor. Parameter routes, login, register and the wildcard 404 route are listed as skipped. Duplicate paths are checked once.

Checks collect console errors and uncaught page errors (V1), API responses at least 500 (V2), main content shorter than 20 normalized characters (V3), crashes/error boundaries/404/login or unexpected forbidden redirects (V4), and root horizontal overflow over one pixel (V5). Navigation and network-idle waiting share a ten-second budget. The report includes full error messages, final URLs, occurrences, screenshots and durations.

V1 messages containing `Failed to load resource` include the method, full URL and HTTP status of matching responses at least 400. Matching uses the console location URL and status when present; without a location, only a unique matching response is attributed. Unmatched or ambiguous console errors remain visible as V1 without inventing response details.

Artifacts are ignored in `.verify-ui/`: `report.json` and `<role>/<route-slug>-<viewport>.png`. Exit 1 means NEW errors or a setup/cleanup failure; KNOWN errors and STALE entries do not fail. Interrupts stop owned process trees, close the browser and release both ports in cleanup, with exit 130/143.

`known-issues.json` contains `{ "route", "viewport", "code", "match" }` entries. `match` is a literal substring of the full message, scoped to the exact route, viewport and code. Do not add entries unless the task explicitly permits updating the baseline. A full run reports every unmatched entry as STALE; filtered runs only consider entries in the requested prefix and viewport scope.

Run the browser-free parser, baseline and slug tests from the repository root:

```sh
node --test scripts/verify/
```
