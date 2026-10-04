# ADR-005: Access control and security for the online API

- Status: Accepted
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

### Questions for the owner (answered)

| # | Question | Owner's answer |
| --- | --- | --- |
| Q1 | Who may use the API? | **Answered.** All accounts of the Google Workspace domain `euromaster.com`, plus one personal Google account of the owner. No open sign-up. The owner confirmed that `euromaster.com` is a Google Workspace domain. |
| Q2 | How do users connect? | **Answered.** API now. A web interface comes later, decided in a separate ADR. The choices here must stay compatible with it (Google login can also serve a web UI). |
| Q3 | Budget? | **Answered.** Free tier, or a small amount per month if needed. |
| Q4 | Hosting platform? | **Answered.** Azure, in the owner's own personal subscription (not Euromaster's tenant), so no Euromaster IT governance applies. Region: an EU region (West Europe or North Europe), so the container and its logs stay in the EU. |

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

Provider: **Google** is chosen (see Decision). Microsoft Entra External ID
(billed per monthly active user, with a free tier [6]) stays only as the
broker alternative described under "Main trade-offs".

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
- Con: ties the security of the API to one platform.
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

## Decision

**Alternative B: an external managed OpenID Connect identity provider, with
JWT bearer validation inside the API.**

- Every endpoint requires an authenticated user by default (a fallback
  authorization policy [4]), so a forgotten endpoint is closed, not open.
- `/health` is the only deliberate exception: it stays anonymous for
  platform probes and returns no data (ADR-004). The OpenAPI document stays
  Development-only.
- Missing, expired or invalid token: `401 Unauthorized` with a
  `WWW-Authenticate` header [4]. No redirect to a login page; this is an API.
- A platform feature (alternative C) may be added later as an **extra**
  layer, never as the only lock.

### Identity provider: Google (refines alternative B)

Google (OpenID Connect), used directly, without a broker.

**Token.** The API accepts a Google **ID token** as
`Authorization: Bearer <token>`. A Google access token is not used: it lets an
application "access all the APIs related to the scopes of access you
requested" [14], which are Google's APIs, not ours.

**Validation** with `AddJwtBearer` [4]:

- Authority / metadata: `https://accounts.google.com`. The discovery document
  is at `https://accounts.google.com/.well-known/openid-configuration` [14];
  the signing keys come from its `jwks_uri`.
- Valid issuers: **both** `https://accounts.google.com` and
  `accounts.google.com`. Google: `iss` is "Always `https://accounts.google.com`
  or `accounts.google.com` for Google ID tokens" [14].
- Valid audience: our Google OAuth client ID. Google: "Verify that the value
  of the `aud` claim in the ID token is equal to your app's client ID" [14].
  The client ID is configuration, not a secret.
- Lifetime and signature are validated as usual.

**Authorization.** A policy, applied through the fallback policy after
authentication, allows the request if **either**:

1. the token has the claim `hd` equal to `euromaster.com`, **or**
2. the token's `sub` is in the configured allow-list of individual users.

Otherwise the API returns `403 Forbidden` (authenticated, but not allowed). A
missing, invalid or expired token stays `401 Unauthorized`.

**Rules:**

- **Never decide access on the domain of the `email` claim.** Google: "you
  can't rely on the domain of the `email` claim to identify users of Google
  Workspace or Cloud organizations; use the `hd` claim instead" [14]. An
  account with an `@euromaster.com` email address but no `hd` claim (a
  personal Google account created with a work address) gets 403. Google: "The
  absence of this claim indicates that the account does not belong to a Google
  hosted domain" [14].
- The `hd` **request** parameter in the login URL is only a UI hint. Google:
  "Don't rely on this UI optimization to control who can access your app, as
  client-side requests can be modified" [14]. Only the `hd` claim in the
  validated token counts.
- Individual users are identified by `sub`, not by email. Google: `sub` is
  "unique among all Google Accounts and never reused", and "the `sub` value is
  never changed"; the email "could change over time" [14]. For the
  owner's personal Google account, the owner looks up the `sub` after the
  first login and adds it to the allow-list.
- Leavers: Euromaster accounts lose access when Euromaster disables them in
  Workspace; no action by us. Individual users are removed from the
  allow-list.

**Configuration** (environment variables or the secret store in production,
`dotnet user-secrets` locally). Nothing user-specific goes into committed
`appsettings*.json`, because the allow-list is personal data.

| Key | Meaning |
| --- | --- |
| `Authentication:Google:ClientId` | Our Google OAuth client ID (the expected `aud`) |
| `Authorization:AllowedHostedDomains` | Allowed `hd` values, e.g. `["euromaster.com"]` |
| `Authorization:AllowedSubjects` | Allowed Google `sub` values of individual users |

### Hosting: Azure Container Apps

Azure Container Apps, Consumption plan, scale to zero, in an EU region (West
Europe or North Europe) of the owner's personal subscription.

- Cost: "The first 180,000 vCPU-seconds, 360,000 GiB-seconds, and 2 million
  requests per subscription per month are free", and "No usage charges apply
  when an application is scaled to zero" [15]. A minimum replica is optional;
  then "usage is charged at a reduced idle rate when a replica is inactive"
  [15].
- The lock stays in the application (JWT validation in the API), so the
  platform remains replaceable. Platform authentication (Easy Auth) is only
  ever an extra layer, never the only lock.

### Why

- It meets "only logged-in users" with real logins, MFA and recovery,
  without us storing any password or long-lived user secret.
- It is the standard approach recommended in the official ASP.NET Core
  documentation [4] and is supported by the framework we already use.
- Google login fits Q1 directly (the Euromaster users already have Google
  Workspace accounts) and can also serve a later web UI (Q2).
- The lock lives in the code, so CI can prove it on every PR, and the
  hosting platform stays replaceable.

### Main trade-offs

- **ID token instead of access token.** Using an ID token as the API
  credential deviates from the Microsoft guidance cited above ("Use OpenID
  Connect 1.0 or an OAuth standard to create access tokens for API access"
  [4]). Accepted for a small project. The alternative is a broker (Firebase
  Auth, Microsoft Entra External ID, Auth0) with Google as login, which issues
  real access tokens for our API. Recorded as a possible later change.
- **Many users.** The whole `euromaster.com` domain may be thousands of
  people, not "a few known people". The per-user **and** the global rate
  limits therefore matter more. The privacy reasoning now also covers
  employees of a company. The owner should check whether Euromaster has rules
  on using company Google accounts for external tools.
- **Dependency on Google.** If Google sign-in is down, nobody can log in.
- **Getting a token.** Clients need an OAuth flow to get a Google ID token.
  For scripts and a future CLI mode this is extra work (see "What could not
  be verified"); it is not needed for the current CLI, which calls the RDW
  directly.
- **Cold start.** After an idle period the first request is slower,
  because the app scales to zero. Accepted. A minimum of one replica (idle
  rate [15]) is the fallback if it becomes a problem.

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

- **Per user**, partitioned on the Google `sub` claim, not on email, IP
  address or raw header values: partitioning on
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
  when the RDW's limits are known (see below). With the whole
  `euromaster.com` domain allowed, the global limit is the main protection
  of the RDW quota.
- **Limits are per app instance.** The official documentation [3] does not
  say whether the limits are shared between instances. The limiters keep
  their counters in the memory of the running app, and a test
  (`TwoAppInstances_DoNotShareLimits`) shows that two instances each have
  their own counters. With N replicas, the global limit is effectively N
  times the configured value. The future hosting ADR must therefore either
  pin the maximum replica count to 1, or explicitly accept that the global
  limit multiplies by the number of replicas.

### Cost controls

- A monthly budget alert in Azure Cost Management on the owner's
  subscription.
- Short retention in Log Analytics.
- The container image is hosted on GitHub Container Registry (public, no
  secrets in it) instead of Azure Container Registry, unless the owner
  prefers otherwise.

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
  path to its own access logs, next to the client IP. The platform is Azure
  Container Apps: its ingress or access logging may record the plate in the
  URL path. Check this when deploying and set the retention. Changing the URL
  shape would be a `v2` change under ADR-004 and is not proposed here.
- Retention: as short as is useful for troubleshooting, set in Log
  Analytics. Logs stay in the chosen EU region (Q4).

### Proving the lock works

- **Automated** (CI, `tests/Rdw.Api.Tests`). Tests use locally signed test
  tokens with the same claims shape as Google ID tokens, never real Google
  and never the RDW. A request to `/api/v1/vehicles/{plate}`:

  | Token | Expected |
  | --- | --- |
  | no token | 401, `IRdwClient` not called |
  | wrong `aud` | 401 |
  | wrong `iss` | 401 |
  | expired | 401 |
  | `hd` = `euromaster.com` | 200 |
  | email ends in `@euromaster.com`, **no** `hd` claim | **403** (the key test) |
  | `hd` = `other-domain.com` | 403 |
  | no `hd`, `sub` in the allow-list | 200 |
  | no `hd`, `sub` not in the allow-list (e.g. another `gmail.com` account) | 403 |

  And `/health` without a token returns 200.
- **By hand (owner)**, after each deployment: run
  `curl -i https://<host>/api/v1/vehicles/X998ZG` without credentials and
  check that the answer is `401 Unauthorized`, not 200, 404 or 503. Repeat
  with a valid token and check that it returns 200. Also log in with a Google
  account that is not allowed and check that it returns `403 Forbidden`.
  Steps go in the README when the feature is built.

### Rollout order

- Build authentication and authorization first, then rate limiting, both
  with tests. Deploy only after that.
- First deployment: only the owner's personal Google account
  (`Authorization:AllowedSubjects`). `Authorization:AllowedHostedDomains`
  stays empty, so Euromaster accounts are denied.
- The `euromaster.com` domain may only be enabled after:
  1. the per-user and global rate limits are implemented and tested;
  2. the owner has checked with Euromaster that using company Google
     accounts for this API is acceptable;
  3. the owner has confirmed that the Workspace admin settings let
     employees sign in to this app.
- Enabling the domain is a configuration change by the owner, not a code
  change.

### Follow-up work (separate issues after acceptance)

1. Owner creates the Azure subscription with a personal Microsoft account
   (not the Euromaster account), sets a monthly budget alert and chooses the
   EU region.
2. Owner creates the Google OAuth client in Google Cloud Console and records
   the client ID in configuration.
3. Owner obtains the `sub` of their personal Google account.
4. Owner decides on enabling secret scanning and push protection.
5. Implement authentication, the fallback policy and the `hd`/`sub`
   authorization policy, with the 401 and 403 tests.
6. Implement rate limiting with 429.
7. Check the Container Apps access logs and set the retention.
8. Update ADR-004 ("Out of scope" lists authentication and rate limiting)
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
  Microsoft Entra External ID on an official page I could read. Only relevant
  if a broker or Cloudflare is chosen later.
- **How clients get a Google ID token.** How scripts and `curl` obtain a
  Google ID token for our client ID was not checked. Google's discovery
  document lists a `device_authorization_endpoint` (checked 2026-10-04), but
  whether the device flow [5] fits our case was not checked.
- **Google ID token lifetime.** The Google page says to check that `exp` has
  not passed [14], but I did not find the lifetime itself stated there.
- **Container Apps details.** The exact per-second prices for the EU
  regions (the pricing page shows them only per selected region), the cold
  start duration, and the cost of Log Analytics were not checked.
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
14. Google for Developers, "OpenID Connect | Sign in with Google" (updated 2026-06-15): <https://developers.google.com/identity/openid-connect/openid-connect>
15. Microsoft Azure, "Azure Container Apps pricing": <https://azure.microsoft.com/en-us/pricing/details/container-apps/>
