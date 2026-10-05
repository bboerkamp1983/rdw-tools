# Research: RDW fair use, rate limits and terms of use

- Issue: #66
- Researched: 2026-10-05 (all pages below accessed on that date)
- Scope: research only. No code, configuration or ADR is changed by this note.

## Summary

Neither the RDW nor Socrata (Tyler Technologies) publishes a numeric rate
limit: the RDW only says performance is "op basis van fair use", and Socrata
says requests without an app token are throttled per IP address with a
`429` response, while requests with an app token are not throttled unless
abusive. The data is CC0 and free, but the RDW's own terms say reusers may
not state that the data comes from the RDW or use its logo, which may
conflict with our README and needs the owner's attention. I propose keeping
the current defaults (10 per user and 60 global per 60 seconds) marked as
unverified and asking the RDW directly (draft below), with an app token and
caching as options for the owner to decide.

Labels used below: **VERIFIED** (read on the cited page, or observed in a
request listed under "Requests sent to the RDW"), **UNVERIFIED** (seen only
indirectly, for example in a search snippet, or a judgment call), **NOT
FOUND** (looked for and no statement exists on the pages checked).

## 1. Does the RDW publish its own fair-use rules or limits?

- **VERIFIED**: the RDW's "Bijsluiter" (terms page for open data) [1] lists
  the conditions. Relevant points, paraphrased:
  - No guarantees on availability, timeliness or continuity of the datasets
    or the platform; the RDW may stop and remove a dataset without
    justification.
  - Building a service on the data, and the business risk if the data stops,
    is at the user's own risk.
  - The RDW charges nothing for open data through this channel.
  - "Performance vindt plaats op basis van fair use" (performance is on a
    fair-use basis). **No definition of fair use and no number is given.**
  - Announcements about the datasets are posted in the RDW Open Data forum
    (Google Group `voertuigen-open-data`).
- **NOT FOUND**: a numeric request limit, a definition of "fair use", or
  anything about app tokens on: the Bijsluiter [1], the RDW open data pages
  "Open data" [2], "Algemene informatie" [3] and "Handleidingen" [4], the
  opendata.rdw.nl home page [5], and the dataset description PDF
  "Beschrijving dataset gekentekende voertuigen v5.4" [6] (searched for
  token, limiet, limit, fair, throttl, 429, requests).

## 2. What does Socrata document about throttling?

opendata.rdw.nl runs on Socrata, now "Data & Insights" by Tyler Technologies
(**VERIFIED**: the response header `X-Socrata-Region: aws-eu-west-1-prod`
and the Tyler links in the footer of [5]).

From "Application Tokens" [7] (**VERIFIED**):

| Topic | What the page says |
| --- | --- |
| Without a token | Throttling is "mainly source IP address"; requests without a token "come from a shared pool via IP address"; IP addresses that make too many requests "during a given period" may be throttled; a "much lower throttling limit" applies to everything from that IP address. |
| With a token | Each application and developer gets "their own pool"; Socrata "currently" does not throttle token requests unless they are "abusive or malicious". |
| Still expected | Be deliberate on a shared platform; applications that are abusive or "otherwise monopolize the use of our API" may be throttled; Socrata will "likely" reach out before throttling. |
| When exceeded | A "status code `429`" response. |
| Changes | Socrata may change the limits "with notice" and will post an update. |
| How to send | `X-App-Token` header (preferred) for SODA 3.0 and 2.x. |

From "Response Codes & Headers" [8] (**VERIFIED**): `429 Too Many Requests`
means the client "is currently being rate limited", with the advice to use an
app token.

- **NOT FOUND**: a number (requests per second, hour or day), the length of
  "a given period", whether the window is fixed or rolling, a `Retry-After`
  header, or how long a block lasts. Neither [7] nor [8] mentions these.
- **VERIFIED** by observation: a normal response from the endpoint we use
  carries no rate limit headers (no `X-RateLimit-*`, no `Retry-After`; see
  "Requests sent to the RDW").
- **UNVERIFIED**: unofficial blog posts and older forum answers quote numbers
  for Socrata limits. I did not use them, because no official page confirms
  them.

### Relevance for our code

- **VERIFIED** (code): `RdwClient` sends exactly one GET per lookup and has
  no retry (`src/Rdw.Core/RdwClient.cs`). A `429` from the RDW is a non-success
  status and becomes `ServiceUnavailable` (our 503), with the status code in
  the message. So a throttled RDW is visible but not distinguished from other
  outages.

## 3. Is an app token required, recommended or optional?

- **VERIFIED**: for the endpoint we use (SODA 2.x, `/resource/m9d7-ebf2.json`)
  a token is **optional but recommended**: queries without a token work
  ("possible to perform simple unauthenticated queries"), but a token gives
  "much higher throttling limits" [7]. Our own request without a token
  returned 200 (see "Requests sent to the RDW").
- **VERIFIED**: for **SODA 3.0** (`/api/v3/views/IDENTIFIER/query.json`),
  query requests "must be either authenticated by a user or marked with a
  valid application token" [9]. We do not use SODA 3.
- **UNVERIFIED**: Tyler's support article "SODA3 API" appears (search
  snippet only) to say SODA 2.1 remains fully supported and that SODA 1 is
  deprecated from 2026-10-01. The page refused automated access (HTTP 403),
  so I could not read it [10]. If SODA 2 is ever retired, a token becomes
  required.
- **Who can request one**: [7] says you get one by "registering for one in
  your Socrata profile". **UNVERIFIED**: whether any member of the public can
  create such a profile on opendata.rdw.nl, and whether it is free. The Tyler
  support article "Generating an App Token" [11] returned HTTP 403.
- I did **not** register or create a token. Any token is a secret
  (ADR-005: Container Apps secret, never in the repository).

## 4. License and terms of use

- **VERIFIED**: the dataset is licensed **Creative Commons Zero (CC0)**: the
  Bijsluiter [1] says so, and the dataset metadata [12] has license
  `Public Domain` and custom field `Licentie: Creative Commons 0 (CC0)`.
- **VERIFIED**: free of charge [1].
- **VERIFIED**: commercial or work use and redistribution are not restricted
  on the pages checked; the user carries the risk of building a service on
  the data [1].
- **VERIFIED, needs the owner's attention**: the Bijsluiter [1] says that, as
  part of CC0, reusers are **not allowed to state that the data comes from
  the RDW** ("niet toegestaan te vermelden dat de gegevens afkomstig zijn van
  de RDW") and **not allowed to use the RDW logo or house style** in the
  applications they build.
  - Our README says "Data source: RDW open data" and the project is named
    `rdw-tools`. Whether a developer README or project name counts as
    "vermelden" in an application is a legal reading I cannot make.
  - The rule seems to aim at not suggesting that the RDW endorses or vouches
    for an application. CC0 itself contains no such rule; this is the RDW's
    own condition.
  - Conflicting signal: the dataset metadata [12] has the attribution field
    `Team Open Data RDW`.
  - Proposal: ask the RDW (see the draft message), and decide after the
    answer whether the README, the API responses or the project name need
    changing. Not changed in this PR.
- **VERIFIED**: the RDW is not liable for damage from using the data, and not
  liable for "onrechtmatige of vrije toegang" to data that you offer through
  the dataset [1]. Our API returns the data to other users at our own risk.
- **VERIFIED**: the vehicle data contains no data that is privacy, fraud or
  competition sensitive, per the RDW [3]. This does not settle whether a plate
  combined with a user identity is personal data (ADR-005 keeps that open).
- Tyler's "Data & Insights SaaS license agreement" is linked from the
  opendata.rdw.nl footer [5]. **NOT CHECKED in detail**: by its title it is
  the agreement between Tyler and its client (the RDW), not terms for API
  consumers.

## 5. Does Azure Container Apps share outbound IP addresses?

Throttling without a token is per IP address, in a "shared pool" [7]. If
our outbound IP were shared with other tenants, their Socrata traffic would
count against the same pool.

- **VERIFIED**: the "outbound public IP" is the "from" address for outbound
  connections, and "Outbound IPs might change over time" [13].
- **VERIFIED**: a fixed outbound address (Azure NAT Gateway) is only
  supported in a workload profiles environment with a custom virtual network
  [13]. ADR-006 creates the environment without a custom virtual network, so
  we have no control over the outbound address.
- **UNVERIFIED / NOT FOUND**: whether the outbound IP of a Consumption
  environment is shared with other customers. The official pages I checked
  ("Networking" [13], "Container Apps environments" [14], "Structure" [15])
  do not say. Microsoft Q&A answers discuss it, but those are not official
  documentation.
- Consequence (judgment call): without a token, we must assume that other
  traffic might share our IP pool and that our address can change. Our own
  limits cannot protect us against other tenants' traffic. An app token
  removes this dependency, because throttling then happens per application
  [7].

## 6. Proposal for the rate limit numbers

### Inputs

| Input | Value | Status |
| --- | --- | --- |
| RDW numeric limit | none published | NOT FOUND [1]-[6] |
| Socrata limit without token | "much lower", per IP, number unknown | VERIFIED that it exists; number NOT FOUND [7] |
| Socrata limit with token | not throttled unless abusive | VERIFIED [7] |
| Signal when exceeded | `429` | VERIFIED [7][8] |
| RDW calls per API request | at most 1 (no retries; 401/403 never reach the limiter or the RDW) | VERIFIED (code) |
| Replicas | at most 1 (ADR-006), so the configured global limit is the real global limit | VERIFIED (ADR-006) |
| Replicas during a revision change | can briefly be more than 1 | VERIFIED (ADR-006, "Replica count") |
| Outbound IP shared with other tenants | unknown | UNVERIFIED |
| Expected number of users | a few people, later possibly the `euromaster.com` domain | ADR-005, not measured |

### Arithmetic with the current defaults

- Global 60 per 60 s = **1 request per second** at most to the RDW, from one
  replica. Worst case per hour: 60 × 60 = 3,600. Per day: 3,600 × 24 = 86,400.
- During a revision change with 2 replicas for a short time: up to 2 per
  second for that period.
- Per user 10 per 60 s: 60 / 10 = **6 users** at their personal maximum
  together reach the global limit. One user alone can use at most
  10 / 60 = 1/6 of the global budget.
- A user looking up plates by hand (say one every 6 seconds, a judgment call)
  stays exactly at 10 per minute, so the per-user limit does not hinder
  normal manual use but stops loops and scraping. Scraping the full dataset
  of 16,865,476 plates (from `rdw-open-questions.md`) at one user's maximum
  would take 16,865,476 / 10 minutes ≈ 3.2 years.

### Proposal

1. **Keep the defaults, marked as unverified**: `RateLimiting:PerUser`
   10 per 60 s and `RateLimiting:Global` 60 per 60 s. There is no verified
   RDW or Socrata number to derive other values from, so any change would be
   another guess. Update the code comment and README wording from "not based
   on RDW limits" to point to this note (separate follow-up issue, after the
   owner agrees).
2. **Why this is probably far below the limit** (judgment call, not
   verified): 1 request per second is the pace of one person clicking through
   a website. Socrata says that with a token it will "likely" contact us
   before throttling [7], and that it acts against applications that
   "monopolize" the API. At 1 per second we are unlikely to be either.
3. **What to watch**: a `429` from the RDW currently becomes our 503 with
   "status 429" in the message. If that ever shows up in the logs, lower the
   global limit and ask the RDW. A separate `LookupStatus` for throttling
   would be a design change and needs an ADR; not proposed now.
4. **Raising the limits** (for example when the `euromaster.com` domain is
   enabled) should wait until either the RDW answers the question below or
   an app token is in use.

### Options for the owner (not decided)

- **App token**: moves us from the shared IP pool to our own pool, which
  also removes the unknown about shared Azure outbound IPs (section 5).
  Costs: a Socrata/Tyler account (who can register is UNVERIFIED), a secret
  to manage (Container Apps secret per ADR-005/006), and a small code change
  to send `X-App-Token`. Needed anyway if we ever move to SODA 3 [9].
- **Caching**: the dataset was last modified on 2026-10-05 05:24 GMT
  (response header `Last-Modified`), which suggests daily updates
  (UNVERIFIED: one observation). Caching a lookup for a few hours would cut
  repeat calls for the same plate. It is a significant design decision
  (stale data, memory, plates in memory as possible personal data) and needs
  its own ADR.

## What I could not verify

- **Any numeric limit**, from the RDW or Socrata, with or without a token;
  the length and kind of the throttling window; whether `429` comes with
  `Retry-After`; how long a block lasts. No official page states these.
- **What the RDW means by "fair use"** [1]. Not defined anywhere I looked.
- **Who can request an app token** for opendata.rdw.nl and whether it is
  free: the Tyler support article [11] returned HTTP 403 to automated access.
- **SODA 2.1 support status and SODA 1 deprecation**: seen only in a search
  snippet; the Tyler support article [10] returned HTTP 403.
- **Whether Azure Container Apps (Consumption, no custom VNet) shares
  outbound IP addresses with other tenants**: not stated in the official
  documentation I checked [13][14][15].
- **How to read the "no mention of the RDW" condition** [1] for our README,
  project name and API responses. This is a legal reading; ask the RDW.
- **Tyler's SaaS license agreement** linked from opendata.rdw.nl: not read in
  detail (it appears to be the agreement with the RDW, not with consumers).

## Draft question to the RDW (Dutch, not sent)

To send through the RDW contact form (link in the footer of [1]) or post in
the RDW Open Data forum. Not sent; the owner decides.

> Onderwerp: Vraag over fair use en bronvermelding bij Open Data RDW
>
> Beste Team Open Data RDW,
>
> Ik bouw een kleine interne toepassing die voertuiggegevens opvraagt uit de
> dataset Gekentekende voertuigen (m9d7-ebf2) via de API op
> opendata.rdw.nl. Het gaat om losse opvragingen per kenteken, met een
> maximum van ongeveer één verzoek per seconde.
>
> In de bijsluiter staat dat de performance op basis van fair use is. Ik heb
> drie vragen:
>
> 1. Hanteert de RDW concrete grenzen voor fair use (bijvoorbeeld een
>    maximum aantal verzoeken per minuut of per dag), met of zonder app
>    token?
> 2. Raadt de RDW aan om een app token te gebruiken, en hoe vraag ik die aan?
> 3. In de bijsluiter staat dat bij hergebruik niet vermeld mag worden dat de
>    gegevens van de RDW afkomstig zijn. Geldt dat ook voor technische
>    documentatie voor ontwikkelaars (bijvoorbeeld een README die noemt
>    welke dataset wordt gebruikt), of alleen voor wat eindgebruikers in de
>    toepassing zien?
>
> Alvast hartelijk dank voor uw antwoord.
>
> Met vriendelijke groet,
> [naam]

## Requests sent to the RDW

All on 2026-10-05, from one machine, by hand. No load or repeated requests.

| # | Request | Purpose | Result |
| --- | --- | --- | --- |
| 1 | `GET https://opendata.rdw.nl/resource/m9d7-ebf2.json?kenteken=X998ZG` (no token) | Look at response headers | 200; no rate limit headers; `X-Socrata-Region: aws-eu-west-1-prod`; `Last-Modified: Mon, 05 Oct 2026 05:24:24 GMT` |
| 2 | `GET https://opendata.rdw.nl/api/views/m9d7-ebf2.json` | Dataset license metadata | `license.name: Public Domain`, `Licentie: Creative Commons 0 (CC0)`, `attribution: Team Open Data RDW` |
| 3 | `GET https://opendata.rdw.nl/` | Find terms and links in the footer | 200 |

Other pages were fetched from `www.rdw.nl` (web pages and one PDF, not the
API), `dev.socrata.com`, `support.socrata.com` (403) and
`learn.microsoft.com`.

## Sources

All accessed 2026-10-05.

1. RDW, "Bijsluiter": <https://www.rdw.nl/over-rdw/dienstverlening/open-data/bijsluiter>
2. RDW, "Open data": <https://www.rdw.nl/over-rdw/dienstverlening/open-data>
3. RDW, "Algemene informatie": <https://www.rdw.nl/over-rdw/dienstverlening/open-data/algemene-informatie>
4. RDW, "Handleidingen": <https://www.rdw.nl/over-rdw/dienstverlening/open-data/handleidingen>
5. Open Data RDW home page: <https://opendata.rdw.nl/>
6. RDW, "Beschrijving dataset gekentekende voertuigen", version 5.4, 24 April 2023 (PDF, linked from [4]): <https://www.rdw.nl/-/media/rdwnl/documenten/informatieverstrekking/3-e-1891c--beschrijving-dataset-gekentekende-voertuigen-v54.pdf>
7. Socrata / Tyler, "Application Tokens": <https://dev.socrata.com/docs/app-tokens.html>
8. Socrata / Tyler, "Response Codes & Headers": <https://dev.socrata.com/docs/response-codes.html>
9. Socrata / Tyler, "API Endpoints": <https://dev.socrata.com/docs/endpoints.html>
10. Tyler support, "SODA3 API" (HTTP 403, not read): <https://support.socrata.com/hc/en-us/articles/34730618169623-SODA3-API>
11. Tyler support, "Generating an App Token" (HTTP 403, not read): <https://support.socrata.com/hc/en-us/articles/210138558-Generating-an-App-Token>
12. Dataset metadata for `m9d7-ebf2`: <https://opendata.rdw.nl/api/views/m9d7-ebf2.json>
13. Microsoft Learn, "Networking in Azure Container Apps environment" (ms.date 2025-06-25): <https://learn.microsoft.com/azure/container-apps/networking>
14. Microsoft Learn, "Azure Container Apps environments" (ms.date 2026-02-26): <https://learn.microsoft.com/azure/container-apps/environment>
15. Microsoft Learn, "Azure Container Apps structure" (ms.date 2025-05-21): <https://learn.microsoft.com/azure/container-apps/structure>
