# ADR-005: Access control and security for the online API

- Status: Proposed
- Date: 2026-10-04
- Relates to: ADR-004 (REST API)

## Context

The REST API (ADR-004) will run online. It must only be available to
logged-in users. Today it has no authentication: anyone who can reach it can
look up any plate, and every lookup costs an RDW call.

Some facts that shape this decision:

- The repository is **public**. Anything committed, including history, is
  readable by anyone.
- The API calls the RDW open data (Socrata). Socrata throttles requests by IP
  address when there is no app token, and does not throttle requests with an
  app token "unless those requests are determined to be abusive or
  malicious" [1]. An app token is therefore something worth protecting:
  "if your application token is duplicated by another developer, their
  requests will count against your quota" [1].
- A license plate is linked to the registered keeper of the vehicle through
  the RDW register. Under the GDPR, personal data is "any information relating
  to an identified or identifiable natural person" [2]. A plate looked up by a
  known user tells something about that user's interest in a vehicle (and,
  indirectly, in its keeper). We treat plates linked to users as personal data.
- The CLI calls the RDW directly through `Rdw.Core`; it does not use the API.
  Locking the API does not affect the CLI unless the CLI is later changed to
  call the API.

### Open questions for the owner

Who the users are and how they connect is not decided yet. The choice below
works for the recommended defaults; the answers may change the details.

| # | Question | Options | Recommended default |
| --- | --- | --- | --- |
| Q1 | Who may use the API? | Only the owner / a few known people / open sign-up | **A few known people, invited by the owner.** No open sign-up: it adds abuse, support and privacy work that the project does not need yet. |
| Q2 | How do users connect? | Web UI / only API and CLI | **API only for now** (scripts, `curl`, a future CLI mode). A web UI is a separate ADR; the choice below must not block it. |
| Q3 | Budget? | Free tiers only / small monthly amount | **Free tiers only** until there is a reason to pay. |
| Q4 | Hosting platform preference? | Azure / another cloud / own server | **No preference yet.** The choice below must work on any platform that runs a container or a .NET app, so it does not decide the platform. |

## Threat model (short)

| Threat | What could go wrong | Main countermeasure |
| --- | --- | --- |
| Credential leak in the public repository | A token, client secret, RDW app token or signing key is committed (also in history, a config file or a container image) and anyone can use it. | No secrets in code, repository or image; configuration from the environment or a secret store; GitHub secret scanning and push protection. |
| Exhausting the RDW limits | One user, a stolen credential or a bug calls the API in a loop. The RDW throttles or blocks our app token or IP, and the API is down for everyone. | Authentication first, then per-user and global rate limits in the API. |
| Scraping | A logged-in user walks through many plates to build a copy of the data or to track vehicles. | Per-user rate limits; the ability to revoke a single user quickly; no bulk endpoint. |
| Personal data in logs | Logs (application, reverse proxy, platform) contain plates next to a user identity, kept longer and shared wider than needed. | Do not log plates together with user identities; keep the existing query redaction; limit log retention. |
| Unauthenticated access | A new endpoint is added without protection, or a configuration change disables the check. | Authentication required by default for every endpoint (fallback policy); an automated test that expects 401; the owner's manual check. |

Out of scope for this ADR: DDoS protection (rate limiting alone is not a DDoS
defence; the platform or a CDN has to provide that [3]), and attacks on the
identity provider itself.

## Alternatives

### A. API keys issued by the owner

The owner creates a long random key per user; the client sends it in a
header; the API compares it with a stored list.

- Pro: simple for scripts and `curl`; no external service.
- Con: we build and maintain key issuance, storage (hashed), rotation,
  expiry and revocation ourselves. A key is a long-lived shared secret
  without MFA; a leaked key works until someone notices. It identifies a key,
  not a person who logged in, so it does not really meet "logged-in users".
- I found no built-in API key authentication handler in ASP.NET Core; this
  would be our own code (see "What could not be verified").

### B. External managed identity provider (OpenID Connect), token checked by the API

Users log in at an identity provider (IdP) that supports OpenID Connect. The
client receives a short-lived access token (JWT) and sends it as
`Authorization: Bearer <token>`. The API validates it with
`Microsoft.AspNetCore.Authentication.JwtBearer` (`AddJwtBearer` with
`Authority` and `Audience`) [4].

- Pro: the IdP handles passwords, MFA, account recovery and brute-force
  protection. The API holds **no** user secrets: it only needs the IdP's
  public signing keys, fetched from its metadata. Tokens are short-lived.
  Works on any hosting platform. The lock is inside the application, so it
  can be tested in CI like any other behavior: an invalid or missing token
  returns 401 [4]. A future web UI can use the same IdP.
- Con: setup at an external provider; clients must obtain a token (for a
  CLI or script this needs an OAuth flow without a browser redirect, such as
  the device authorization grant [5]); one more external dependency.
- Microsoft's guidance: "Don't generate your own access tokens or ID tokens,
  except for testing purposes" and "Use OpenID Connect 1.0 or an OAuth
  standard to create access tokens for API access" [4].

Possible providers include Microsoft Entra External ID (billed per monthly
active user, with a free tier [6]) and others; the provider is chosen after
Q3 and Q4 are answered.

### C. Access control in front of the application (reverse proxy or platform feature)

A component in front of the API blocks unauthenticated requests before they
reach it. Examples:

- **Azure App Service / Azure Functions built-in authentication
  ("Easy Auth")**: runs as platform middleware before the app, supports
  Microsoft Entra, Google, GitHub, Apple and any OpenID Connect provider, needs
  no code changes, and can return `401 Unauthorized` for unauthenticated API
  requests [7].
- **Cloudflare Access**: puts an identity check in front of the application;
  automated clients use service tokens sent as `CF-Access-Client-Id` and
  `CF-Access-Client-Secret` headers [8].

- Pro: little or no code; the platform handles login and sessions.
- Con: ties the security of the API to one platform (conflicts with Q4).
  The lock is configuration outside this repository, so our tests cannot
  prove it; if the app is also reachable directly (bypassing the proxy), it is
  open. Service tokens are again long-lived shared secrets.

### D. Our own user and password management (rejected)

We do **not** build user accounts with passwords in this application. Doing
it safely means taking on, at least: secure password storage, password
strength and breached-password checks, transport over TLS only, account
recovery, brute-force protection (lockout, MFA), re-authentication for
sensitive actions, generic error messages against user enumeration, and
logging of authentication failures [9]. Every one of those is a place to get
it wrong, and keeping them current is ongoing work. Microsoft adds: "You
should **NOT** create an access token from a username/password request"
[4], and App Service notes that a secure authentication solution "can take
significant effort" and must stay "up to date with the latest security,
protocol, and browser updates" [7]. For a small project with a public
repository and a non-programmer owner, that risk and effort are not
justified when managed providers do it as their core business.

## Decision (proposed)

**Alternative B: an external managed OpenID Connect identity provider, with
JWT bearer validation inside the API.**

- Every endpoint requires an authenticated user by default (a fallback
  authorization policy [4]), so a forgotten endpoint is closed, not open.
- `/health` is the only deliberate exception: it stays anonymous for
  platform probes and returns no data (ADR-004). The OpenAPI document stays
  Development-only.
- Missing, expired or invalid token: `401 Unauthorized` with a
  `WWW-Authenticate` header [4]. No redirect to a login page; this is an API.
- Access is limited to the invited users (Q1), configured at the IdP or as
  an allow-list of user identities in configuration.
- A platform feature (alternative C) may be added later as an **extra**
  layer, never as the only lock.

### Why

- It meets "only logged-in users" with real logins, MFA and recovery,
  without us storing any password or long-lived user secret.
- It is the standard approach recommended in the official ASP.NET Core
  documentation [4] and is supported by the framework we already use.
- It is platform-independent (Q4 open) and keeps the door open for a web
  UI (Q2).
- The lock lives in the code, so CI can prove it on every PR.

### Main trade-offs

- More setup than API keys, and an external dependency: if the IdP is down,
  nobody can log in.
- Clients need an OAuth flow to get a token. For scripts and a future CLI
  mode this is extra work (device authorization grant [5]); it is not needed
  for the current CLI, which calls the RDW directly.
- Possible cost above the provider's free tier; to be checked when the
  provider is chosen.

## Consequences

### Secrets

- Never in code, the repository, `appsettings*.json`, test files or a
  container image. Microsoft: "Never store passwords or other sensitive data
  in source code or configuration files" and "Secrets shouldn't be deployed
  with the app" [10].
- With alternative B the API itself needs no secret for token validation
  (public keys from the IdP metadata). The IdP authority and audience are
  configuration, not secrets.
- The RDW app token (if we add one, [1]) and any client secret come from
  environment variables or the platform's secret store in production, and
  from `dotnet user-secrets` locally. User secrets are for development only
  and not encrypted [10].
- GitHub: secret scanning runs for public repositories [11]; push protection
  for users is enabled by default and stops pushing secrets to public
  repositories [12]. On 2026-10-04 the repository settings API reported
  secret scanning and repository push protection as `disabled` for this
  repository. Enabling them is a repository setting and therefore a decision
  for the owner (see CLAUDE.md, "Stop and ask first").
- If a secret leaks anyway: revoke and rotate it first, then clean up.
  Removing it from Git history alone is not enough.

### Rate limiting

Using ASP.NET Core's built-in rate limiting middleware
(`Microsoft.AspNetCore.RateLimiting`) [3]:

- **Per user**, partitioned on the authenticated user identity (the token
  subject), not on IP address or on raw header values: partitioning on
  unauthenticated input lets an attacker create unlimited partitions [3].
  Authentication runs before rate limiting, so anonymous requests are already
  rejected with 401.
- **Global**, one limit for all users together, below what we expect the
  RDW to accept, so that the API as a whole cannot get our app token or IP
  blocked.
- Rejections return `429 Too Many Requests` with `Retry-After`, set
  explicitly with `RejectionStatusCode` or `OnRejected` [3], so that a rate
  limit is never confused with the 503 "RDW unavailable" of ADR-004.
- The numbers (per minute, per day) are set in configuration and decided
  when the RDW's limits are known (see below).

### Logging

- **Do log**: authentication failures (count, reason category, time), rate
  limit rejections per user identity, RDW errors and timeouts, and the
  ADR-004 `traceId`.
- **Do not log**: plates together with user identities, tokens or
  `Authorization` headers, client secrets, the RDW app token, full request
  URLs that contain a plate. This follows OWASP's list of data to exclude
  from logs (access tokens, session identifiers, secrets, sensitive personal
  data) [13].
- Current state (checked on 2026-10-04 by running the API): a lookup does
  not write the plate to the log. `IHttpClientFactory` logs the RDW URL with
  the query string masked (`...m9d7-ebf2.json?*`), and ASP.NET Core request
  logs are at `Warning`. Keep it that way, and add a test or a review check
  when logging changes.
- The plate is part of the URL path (`/api/v1/vehicles/{licensePlate}`,
  ADR-004). A reverse proxy or hosting platform will usually write that
  path to its own access logs, next to the client IP. When the platform is
  chosen, its access logging and retention must be checked. Changing the URL
  shape would be a `v2` change under ADR-004 and is not proposed here.
- Retention: as short as is useful for troubleshooting; to be set with the
  platform.

### Proving the lock works

- **Automated** (CI, `tests/Rdw.Api.Tests`): a request to
  `/api/v1/vehicles/{plate}` without a token returns 401 and does not call
  `IRdwClient`; an invalid or expired token returns 401; a valid test token
  returns the normal result; `/health` returns 200 without a token. Tests use
  locally created test tokens, never a real IdP and never the RDW.
- **By hand (owner)**, after each deployment: run
  `curl -i https://<host>/api/v1/vehicles/X998ZG` without credentials and
  check that the answer is `401 Unauthorized`, not 200, 404 or 503. Repeat
  with a valid token and check that it returns 200. Steps go in the README
  when the feature is built.

### Follow-up work (separate issues after acceptance)

1. Owner answers Q1–Q4; choose the identity provider.
2. Owner decides on enabling secret scanning and push protection.
3. Implement authentication and the fallback policy, with the 401 tests.
4. Implement rate limiting with 429.
5. Check the platform's access logs and retention.
6. Update ADR-004 ("Out of scope" lists authentication and rate limiting)
   and the README.

## What could not be verified

- **RDW-specific limits.** The throttling rules cited are Socrata's general
  documentation [1]. I found no official RDW page that states the RDW's own
  limits or fair-use terms. Before setting the global rate limit, check this
  on `opendata.rdw.nl` or ask the RDW.
- **License plates as personal data.** The Dutch Data Protection Authority
  (Autoriteit Persoonsgegevens) page on traffic cameras appears to say that
  plates are personal data for anyone with access to the RDW register, but
  the site refused automated access (HTTP 403) and I could not read the
  exact text. The reasoning above rests on the GDPR definition [2] and should
  be confirmed by the owner or a privacy adviser.
- **Free tier sizes and prices.** I did not find the number of free users
  for Cloudflare Access or the number of free monthly active users for
  Microsoft Entra External ID on an official page I could read. Both must be
  checked on the providers' pricing pages when choosing.
- **Device authorization grant support** per identity provider was not
  checked; it depends on the provider chosen.
- **No API key handler in ASP.NET Core.** I did not find one in the
  documentation, but did not check the full list of built-in handlers.
- **GitHub secret scanning state.** The documentation says secret scanning
  runs for public repositories [11], while the repository settings API
  reports it as `disabled` for this repository. I could not resolve this
  difference; the owner can check it under Settings → Code security.

## Sources

Accessed 2026-10-04.

1. Socrata, "Application Tokens": <https://dev.socrata.com/docs/app-tokens.html>
2. Regulation (EU) 2016/679 (GDPR), Article 4(1): <https://eur-lex.europa.eu/eli/reg/2016/679/oj>
3. Microsoft Learn, "Rate limiting middleware in ASP.NET Core" (updated 2026-09-03): <https://learn.microsoft.com/aspnet/core/performance/rate-limit>
4. Microsoft Learn, "Configure JWT bearer authentication in ASP.NET Core" (updated 2026-09-18): <https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication>
5. RFC 8628, "OAuth 2.0 Device Authorization Grant": <https://datatracker.ietf.org/doc/html/rfc8628>
6. Microsoft Learn, "External ID pricing" (updated 2026-06-22): <https://learn.microsoft.com/entra/external-id/external-identities-pricing>
7. Microsoft Learn, "Authentication and authorization in Azure App Service and Azure Functions" (updated 2025-03-28): <https://learn.microsoft.com/azure/app-service/overview-authentication-authorization>
8. Cloudflare docs, "Service tokens": <https://developers.cloudflare.com/cloudflare-one/identity/service-tokens/>
9. OWASP Cheat Sheet Series, "Authentication": <https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html>
10. Microsoft Learn, "Safe storage of app secrets in development in ASP.NET Core" (updated 2026-05-13): <https://learn.microsoft.com/aspnet/core/security/app-secrets>
11. GitHub Docs, "About secret scanning": <https://docs.github.com/en/code-security/secret-scanning/introduction/about-secret-scanning>
12. GitHub Docs, "Push protection": <https://docs.github.com/en/code-security/concepts/secret-security/push-protection>
13. OWASP Cheat Sheet Series, "Logging": <https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html>
