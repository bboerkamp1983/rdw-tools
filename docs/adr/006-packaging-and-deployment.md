# ADR-006: Packaging and deployment

- Status: Accepted
- Date: 2026-10-04 (proposed and accepted)
- Relates to: ADR-004 (REST API), ADR-005 (access control and security)

The owner accepted this ADR with the answers in "Owner decisions" and asked for
four additions before acceptance: replica limits as a target
("Replica count"), rollback and an off switch ("Rollback and taking the app
offline"), owner account security ("Owner account and subscription access"),
and the behavior when Google's keys cannot be fetched ("When Google's signing
keys cannot be fetched").

## Context

### Already decided in ADR-005

- **Hosting**: Azure Container Apps, Consumption plan, scale to zero, in an
  EU region (West Europe or North Europe) of the owner's **personal** Azure
  subscription, so the container and its logs stay in the EU.
- **The lock stays in the application** (Google ID token validation and the
  `hd`/`sub` rules). Platform authentication (Easy Auth) is only ever an extra
  layer.
- **Rate limits are per app instance**: each replica keeps its own counters,
  so with N replicas the global limit multiplies by N. This ADR must pin the
  replica count to 1 or accept the multiplication.
- **Rollout order**: authentication and rate limiting first (done: #56, #60),
  then deploy. The first deployment allows only the owner's personal Google
  account (`Authorization:AllowedSubjects`); `Authorization:AllowedHostedDomains`
  stays empty, so Euromaster accounts are denied.
- **Cost controls**: a monthly budget alert, short Log Analytics retention,
  and the image on GitHub Container Registry unless the owner prefers
  otherwise.
- **Logging**: the plate is in the URL path; platform access logs may record
  it. This must be checked and the retention set.

### Still open

- How the API is packaged, and where the image is stored.
- The replica limit, and how to configure and check it.
- Where each setting comes from in Azure.
- Whether and how CI builds and publishes the image (`.github` is not changed
  by this ADR; changes there need the owner's approval).
- Log destination and retention; expected monthly cost.
- The steps of the first deployment and the checks afterwards.

## Packaging alternatives

### A. Container image built by the .NET SDK, without a Dockerfile (chosen)

`dotnet publish --os linux --arch x64 /t:PublishContainer` builds an image
straight from the project. "The .NET SDK creates container images without
Docker", and it can "push it directly to a container registry without using
any container runtime at all" [1].

- For an ASP.NET Core project the base image is `mcr.microsoft.com/dotnet/aspnet`
  with the tag of the target framework [2].
- On Linux, for .NET 8 or higher, the container runs as "the rootless user
  `app`" by default [2].
- Pro: no Dockerfile to maintain; secure defaults (non-root, Microsoft base
  image); works the same locally and in CI; the base image follows the
  target framework.
- Con: no `RUN` steps ("There's no way of performing `RUN` commands with the
  .NET SDK" [2]); we do not need any.

### B. Container image from a multi-stage Dockerfile

- Pro: full control over every layer; the most common approach, so many
  examples exist.
- Con: one more file to keep in step with the project (base image tags, user,
  ports); easier to make the image run as root or copy too much into it by
  mistake; Docker or another builder is needed in CI.

### C. Self-contained executable (no container)

- Con: Azure Container Apps runs container images only ("Any Linux-based
  x86-64 (`linux/amd64`) container image" [3]). A plain executable would need
  another host (App Service or a virtual machine), which reverses the
  hosting decision of ADR-005.
- Pro: none for this project, given ADR-005.

### Choice

**A.** It needs no extra file, runs as non-root by default and fits Container
Apps. The image is built for `linux-x64`. Each image gets a **unique tag**
(the Git commit SHA), not `latest`: Container Apps documentation advises
against static tags because they "can lead to caching problems and can make
your app difficult to troubleshoot" [3].

The image contains only the published app and the committed
`appsettings*.json` files, which hold logging settings only. It contains no
secrets and no personal data.

## Replica count

**Decision: at most one replica.** Minimum 0 (scale to zero),
maximum 1.

- The platform default is **10** maximum replicas [4], so the maximum must be
  set explicitly.
- Configure with `--min-replicas 0 --max-replicas 1` on
  `az containerapp create`/`update` [4], and keep the default single revision
  mode.

Known exceptions, accepted:

- **Deployments.** In single revision mode "the existing active revision isn't
  deactivated until the new revision is ready" [5]. For a short time the old
  and the new revision both run, each with its own counters.
- **Platform maintenance.** "During platform upgrades or maintenance, you
  might temporarily see more replicas than expected" [4].
- **Restarts and new revisions reset the counters**, because they live in
  memory (ADR-005).

So for short periods the effective global limit can be up to twice the
configured value. This is accepted: the periods are short and rare, and a
scale-out to more replicas cannot happen through load.

### The limit is a target, not a guarantee

The scaling documentation lists as a known limitation: "Replica quantities
are a target amount, not a guarantee" [4]. Consequences:

- **The global rate limit is best-effort protection of the RDW.** It assumes
  one replica, and the platform does not promise that. It must not be the
  only protection. The other layers are: the allow-list (at first only the
  owner's account, ADR-005), the per-user limit, the 15-second timeout on RDW
  calls, the off switch below, and the open work on the RDW's own fair-use
  rules (#66) and a load test (#67).
- **The replica cap bounds the worst-case compute cost.** Billing is per
  replica: vCPU-seconds and GiB-seconds while a replica runs [15]. With at
  most one replica of 0.25 vCPU and 0.5 GiB, a full month (30 days) of
  continuous running is at most 648,000 vCPU-seconds and 1,296,000
  GiB-seconds, of which 180,000 and 360,000 are in the free grant [15]. The
  euro amount above the grant was **not verified** (see Cost control). This
  bound is itself approximate: during deployments and maintenance more
  replicas can run for a short time [4][5].
- The cap does **not** bound request charges (requests above the free
  2 million a month [15]) or log ingestion. The budget alert and the off
  switch cover those.

How it is checked:

- After every deployment:
  `az containerapp show -n <app> -g <rg> --query "properties.template.scale"`
  must show `"maxReplicas": 1` and `"minReplicas": 0`.
- The deployment steps (and a future CI deploy step) always pass
  `--max-replicas 1`, so a change cannot silently return to the default of 10.
- The in-memory behavior itself is already covered by
  `TwoAppInstances_DoNotShareLimits` (ADR-005).

## Secrets and configuration

Nothing goes into the image or the repository. Settings are environment
variables of the container app. "All platforms support the double
underscore (`__`) syntax and automatically replace it with a colon (`:`)"
[19]. Values that are personal data are stored as
Container Apps **secrets** and referenced with `secretref:` [6].

| Setting (ADR-005) | Environment variable | Where it comes from | Why |
| --- | --- | --- | --- |
| `Authentication:Google:ClientId` | `Authentication__Google__ClientId` | Plain environment variable | Not a secret (ADR-005). |
| `Authorization:AllowedSubjects` | `Authorization__AllowedSubjects__0`, `__1`, ... | Container Apps secret, referenced with `secretref:` | Personal data: kept out of the revision template and the portal's plain view. |
| `Authorization:AllowedHostedDomains` | `Authorization__AllowedHostedDomains__0` | **Not set** at first deployment (rollout order) | Enabling `euromaster.com` later is a configuration change by the owner (ADR-005). |
| `RateLimiting:PerUser:*`, `RateLimiting:Global:*` | `RateLimiting__PerUser__PermitLimit` etc. | Plain environment variables, only if the defaults change | Not secret; defaults are in code. |

How the owner sets them: in the Azure portal (container app > **Secrets**, and
**Containers > Environment variables** when creating a revision), or with
`az containerapp create/update --secrets ... --env-vars ...` [6]. Real values
are never written in this repository, an issue or a commit.

Notes:

- Secrets are "scoped to an application", and changing them does not create a
  new revision; a revision must be restarted or redeployed before the
  container sees a new value [6].
- Roles such as **Container Apps Contributor** and **Container Apps Operator**
  can read secret values in plain text [6]. Only the owner gets access to the
  subscription (see "Owner account and subscription access").
- Azure documentation advises Key Vault references instead of direct secret
  values in production [6]. There is no real secret yet (no RDW app token,
  no client secret), so this ADR uses Container Apps secrets now and Key
  Vault when the first real secret arrives (decision Q3).
- `ASPNETCORE_ENVIRONMENT` is not set, so the app runs as `Production` ("If
  the environment isn't set, it defaults to the `Production` environment"
  [20]): user secrets are not loaded and the OpenAPI document is not served.

## Image storage

| | GitHub Container Registry (ghcr.io), public package | Azure Container Registry (Basic) |
| --- | --- | --- |
| Cost | Public packages are free; container image storage and bandwidth "is currently free", with at least one month's notice before any change [7]. | No free tier on the pricing page; the Basic price shows only as a placeholder there [8]. **Not verified.** |
| Exposure | Public: anyone can pull the image anonymously [9]. The repository is public already, so the image reveals nothing new. A new package is **private** by default and must be made public once [9]. | Private. |
| Secrets to pull | None: Container Apps pulls a public image without registry credentials. A private package would need a username and token stored as a Container Apps secret [3]. | None: Container Apps can pull with a managed identity that has the `acrPull` role [3]. |
| Secrets to push | From GitHub Actions with the built-in `GITHUB_TOKEN` [9]; no stored secret. | Needs Azure credentials in GitHub, or a manual push by the owner. |

**Choice: GitHub Container Registry, public package**, as already preferred in
ADR-005. It costs nothing, needs no secret on either side, and fits a public
repository. Either way the image must contain no secrets; with a public image
this is simply visible to everyone.

## CI (`.github` is not changed by this ADR)

Accepted (Q2): CI publishes an image, it does not deploy. The workflow change
is made in a **separate PR** that the owner approves, not in the PR of this
ADR. Until then, the first deployment may use an image the owner builds and
pushes by hand (First deployment, step 5). The change:

- A job that runs **only on pushes to `main`**, after `build-and-test` has
  passed.
- It runs `dotnet publish src/Rdw.Api --os linux --arch x64 /t:PublishContainer`
  with `ContainerRegistry=ghcr.io`, `ContainerRepository=<owner>/rdw-tools-api`
  and `ContainerImageTag=<commit SHA>` [1][2].
- It authenticates with the built-in `GITHUB_TOKEN` and needs the
  `packages: write` permission for that job only [9]. No stored secret.
- **No automatic deployment to Azure** at first: the owner deploys a chosen
  tag by hand (steps below). That keeps Azure credentials out of GitHub.
  Automatic deployment can be a later ADR.

Cost: GitHub Actions "usage is free for ... public repositories that use
standard GitHub-hosted runners" [10], and the registry is free as above.

## Logging and personal data

Container Apps has three log types: console logs, system logs, and **HTTP
logs**, which "the ingress layer emits ... when HTTP logging is enabled
through diagnostic settings" [11]. The HTTP log schema includes `Path`
("Request path including query string") and `XForwardedFor` ("Contains
end-user IPs, so treat as PII") [11]. On our API the path contains the plate.

Decision (Q6):

- **Do not enable HTTP logs.** Do not create diagnostic settings that send
  them anywhere. Then no plate and no client IP is stored by the platform.
- **Log destination: Log Analytics** for console and system logs [12]. Our
  app logs no plates (ADR-005, checked); rate limit rejections log only the
  `sub`.
- **Retention: 30 days**, with `immediatePurgeDataOn30Days` set to `true`,
  because "Workspaces with 30-day retention might keep data for 31 days"
  [13]. Shorter is possible (down to 4 days), but "lowering the retention
  period below 31 days doesn't reduce costs" [13]. The alternative "Don't
  save logs" stores nothing but leaves only the live log stream for
  troubleshooting [12]; not chosen.

How to check, after the first deployment and after any logging change:

1. The environment's **Diagnostic settings** list is empty, or contains no
   HTTP log category.
2. In Log Analytics, the table `ContainerAppHTTPLogs` does not exist or has
   no rows.
3. After calling `/api/v1/vehicles/X998ZG` a few times, a search for
   `X998ZG` in `ContainerAppConsoleLogs_CL` and `ContainerAppSystemLogs_CL`
   returns no rows.
4. The workspace retention shows 30 days.

## Cost control

- **Budget alert**: a monthly budget on the subscription with email alerts
  (decision Q5: 10 euro, alerts at 50%, 80% and 100% of actual cost, and 100% of
  forecast). A budget does **not** stop spending: "Resources aren't affected,
  and your consumption isn't stopped". Cost data "is typically available
  within 8-24 hours", and a new subscription can take "up to 48 hours" before
  budgets are available [14].
- **Scale to zero**: "When a revision is scaled to zero replicas, no resource
  consumption charges are incurred" [15]. After the last request the app
  waits a cool down period (300 seconds in the scale behavior table [4])
  before scaling to zero.
- **Cold start**: the first request after an idle period starts a replica and
  is slower. Accepted (ADR-005). The duration was **not verified**. The
  fallback is `--min-replicas 1`, billed at a reduced idle rate when the
  replica is idle [15], which uses up the free grant faster.
- **Expected monthly cost: about 0 euro at low use.** The free grant per
  subscription per month is 180,000 vCPU-seconds, 360,000 GiB-seconds and
  2 million requests [15]. With the smallest size, 0.25 vCPU and 0.5 GiB [3],
  that is 720,000 replica-seconds (200 hours) of running time per month, and
  "health probe requests aren't billable" [15]. Log Analytics includes "the
  first 5 GB/month per billing account" and 31 days of retention [16]. GHCR
  and public-repository CI are free [7][10]. **Above the free grants the
  prices were not verified**: the pricing pages showed placeholders [8][16].
  The budget alert is the safety net.

## Rollback and taking the app offline

Every command here changes Azure resources, so Azure asks for multifactor
authentication [23]. The owner needs their second factor at hand to use the
off switch.

### Rollback to the previous image

Changing the image is a revision-scope change: it creates a new revision [5].
In single revision mode the old revision keeps all traffic until the new one
is ready, and "If an update fails, traffic remains pointed to the old
revision" [5].

1. Find the previous image tag:
   `az containerapp revision list -n <app> -g <rg> --all`
   (`--all` "Show inactive revisions" [22]). The image of each revision is in
   its container settings (`properties.template.containers`; the exact field
   path in the CLI output is **not verified**).
2. Deploy that tag again, with the replica limits passed explicitly (see
   Replica count):
   `az containerapp update -n <app> -g <rg> --image ghcr.io/<owner>/rdw-tools-api:<previous-sha> --min-replicas 0 --max-replicas 1`
   [21].
3. Run the manual checks.

Rollback needs the old image: **old package versions in GHCR are not
deleted.** Secrets are application-scope and are not changed by a rollback
[5]. Whether `update` with only `--image` keeps the existing environment
variables is **not verified** in the documentation; the manual checks (401,
200, 403) show it.

### Off switch: stop all traffic

**Disable ingress** [17][24]:

- CLI: `az containerapp ingress disable -n <app> -g <rg>` [24]
- Portal: container app > **Settings > Ingress**, clear **Enabled**, **Save**
  [17].

Ingress is an application-wide setting: "Changes to ingress settings apply to
all revisions simultaneously, and don't generate new revisions" [17]. The
scale and container settings of the revision are not touched.

### The scale-to-zero warning

The scaling documentation warns: "Make sure you create a scale rule or set
`minReplicas` to 1 or more if you don't enable ingress. If ingress is
disabled and you don't define a `minReplicas` or a custom scale rule, your
container app scales to zero and has no way of starting back up" [4].

Our app matches that case while it is offline: no custom scale rule (only
the default HTTP rule [4]) and a minimum of 0 replicas. We set `minReplicas`
to 0 explicitly, but 0 is also the default [4], so we assume the warning
applies (**not verified** whether an explicit 0 counts as "defined").

How the procedure deals with it:

- **While offline, this is the intended effect**: no replica runs, so
  compute charges stop after the cool down period (300 seconds [4]). We do
  **not** set `minReplicas` to 1 while offline: that would keep a replica
  running and billed with nothing able to reach it.
- **The warning is about staying at zero while ingress is disabled.**
  Turning the app back on therefore always starts with re-enabling ingress,
  so HTTP requests reach the app again and the default HTTP scale rule can
  start a replica. The documentation implies this (the warning applies when
  ingress is disabled) but does not state it; **not verified**. The check
  below (`/health` answers `200`) proves it each time.
- If `/health` does not answer after re-enabling: run
  `az containerapp revision list -n <app> -g <rg>` [22] to see the revision's
  running status ("Scale to 0", "Activating", "Activation failed" [5]), and
  restart it with
  `az containerapp revision restart -n <app> -g <rg> --revision <revision>`
  [22]. Whether a restart starts a replica in this situation was **not
  verified**. Do not change `--min-replicas` to get out of it; if a
  temporary `--min-replicas 1` is ever used, set it back to 0 and run the
  replica check.

### Turning the app back on

**Re-enable ingress** with the same settings as at creation:

- CLI:
  `az containerapp ingress enable -n <app> -g <rg> --type external --target-port 8080 --transport auto`
  [24]. This follows the documented example of `az containerapp ingress
  enable` [24], without `--allow-insecure`, so HTTP stays redirected to
  HTTPS (default `false` [24]).
- Portal: container app > **Settings > Ingress**, set **Ingress** to
  **Enabled**, select **Accepting traffic from anywhere**, **Ingress type**
  **HTTP**, **Transport** **Auto**, leave **Insecure connections** cleared,
  **Target port** `8080`, **Save** [17].

**Check afterwards** (also in Manual checks):

| Check | Expected |
| --- | --- |
| `curl -i https://<host>/health` | `200`, `Healthy`. The first request may be slow (cold start); retry for a few minutes before treating it as failed. |
| `az containerapp ingress show -n <app> -g <rg>` [24] | `external` is `true`, `targetPort` is `8080`, `allowInsecure` is `false`, `transport` is `Auto` (property names as in the ingress settings [17]) |
| `az containerapp show -n <app> -g <rg> --query "properties.template.scale"` | `minReplicas` 0, `maxReplicas` 1 |
| `curl -i https://<host>/api/v1/vehicles/X998ZG` (no credentials) | `401` (the lock is still in place) |

Not verified:

- **How quickly** disabling and re-enabling take effect, and what a caller
  sees while ingress is disabled. The documentation gives no duration and no
  status code; the manual check records what happens.
- That `ingress enable` restores the **same address** (`<host>`) as before.
  The `/health` check on the old address shows it.
- The exact layout and letter case of the `ingress show` output (for example
  `Auto` or `auto`); the CLI reference shows no sample output.

Options considered and **not** used:

- `--max-replicas 0`: not allowed, the minimum value for maximum replicas
  is 1 [4].
- `az containerapp stop`: not found in the official Azure CLI reference for
  `az containerapp` [21] (checked 2026-10-04). Not relied on.
- `az containerapp revision deactivate` [22]: the revisions documentation
  describes activating and deactivating for multiple revision mode [5]; the
  effect in single revision mode was **not verified**.
- Last resort: delete the container app. It also removes its secrets and
  configuration, so the first-deployment steps must be repeated.

## Owner account and subscription access

**Before creating the subscription**, the owner turns on two-step
verification on the personal Microsoft account [25]:
<https://account.microsoft.com/security> > **Manage how I sign in** >
**Additional security** > **Two-step verification** > **Turn on**.

- Keep "three pieces of security info associated with your account". Losing
  the second factor can mean "it can take you 30 days to regain access" [25],
  and during that time the off switch cannot be used.
- Azure itself requires multifactor authentication for the Azure portal and
  for create, update and delete operations from the Azure CLI, with no way to
  opt out [23]. Turning it on first means the account is protected before the
  subscription exists, rather than relying on Azure's prompt. **Not
  verified**: the enforcement page describes Microsoft Entra tenants; it does
  not mention personal Microsoft accounts specifically.

**Who gets access**: only the owner's personal Microsoft account, as the one
**Owner** of the subscription. No other users, no guests, no Euromaster
account, no service principals or managed identities with a role on the
subscription, and no Azure credentials in GitHub (CI only pushes to GHCR).
Claude and other AI agents get no Azure access; they do not run `az`
commands. This follows least privilege and stays under Microsoft's advice of
"a maximum of 3 subscription owners" [26]; it also matters because
Container Apps Contributor and Operator can read secrets in plain text [6].

Check (Manual checks): Subscriptions > the subscription > **Access control
(IAM)** > **Role assignments** shows one Owner, the owner's account, and no
other assignments [27]. **Not verified**: that the account that creates a
personal subscription automatically gets the Owner role; the check shows it.

## When Google's signing keys cannot be fetched

The API does not contain Google's keys. On the first request that carries a
bearer token (not at startup), it downloads Google's discovery document
(`https://accounts.google.com/.well-known/openid-configuration`) and the keys
it points to. Google rotates these keys; "examine the `Cache-Control` header
in the response to determine when you should retrieve them again" [28]. After
a scale to zero or a restart the keys are fetched again.

Microsoft's documentation does not describe what happens when this download
fails. **Observed behavior** (local experiment on 2026-10-04, not part of the
repository; the production configuration with Google's address replaced by a
fake handler that threw a connection error, or answered `503`; resolved
package version Microsoft.IdentityModel.Protocols.OpenIdConnect 8.19.2):

| Request while Google is unreachable | Result |
| --- | --- |
| No token | `401`; no download attempted |
| `/health` | `200` |
| Valid token | `401`, empty body; RDW not called. Logged at Information level as `IDX10500: ... No security keys were provided` |
| Same token again | `401`; the download is attempted again |
| Same token after Google is reachable again | Normal answer; no restart needed |

So the API **fails closed**: it answers `401`, never `200` and not `500`.
Downsides: a user with a valid token sees `401` as if the token were wrong,
and the log does not say that Google was unreachable (relevant to #63). The
extra delay on the first request after a cold start was **not measured**.

**A test is needed**: this is library behavior, not our code, and a package
update could change it. Issue #70 adds a regression test with a fake
backchannel (no call to Google) and is a prerequisite for the first
deployment.

## First deployment (owner's personal account only)

Prerequisites: issue #65 (a real Google ID token works locally) and issue #70
(key-fetch failure test) are done. All values in `<angle brackets>` are
filled in by the owner and never committed.

1. **Account and subscription**: turn on two-step verification on the
   personal Microsoft account (not the Euromaster account) first (Owner
   account and subscription access). Then create the Azure subscription with
   that account and create the monthly budget alert (Cost control).
2. **Resource group** in West Europe (Q4), which is in the Netherlands
   (paired region North Europe, in Ireland) [29]:
   `az group create -n <rg> -l westeurope`
3. **Log Analytics workspace** in the same region; set retention to 30 days
   and `immediatePurgeDataOn30Days` to `true` [13].
4. **Container Apps environment** with `--logs-destination log-analytics` and
   that workspace [12]. Do not add diagnostic settings.
5. **Image**: CI publishes `ghcr.io/<owner>/rdw-tools-api:<sha>` (after the CI
   change is approved), or the owner runs the same `dotnet publish` locally
   after `docker login ghcr.io`. Make the package public once [9].
6. **Container app**:
   ```sh
   az containerapp create -n <app> -g <rg> --environment <env> \
     --image ghcr.io/<owner>/rdw-tools-api:<sha> \
     --ingress external --target-port 8080 \
     --cpu 0.25 --memory 0.5Gi \
     --min-replicas 0 --max-replicas 1 \
     --secrets "allowed-subject-0=<owner-sub>" \
     --env-vars "Authentication__Google__ClientId=<client-id>" \
                "Authorization__AllowedSubjects__0=secretref:allowed-subject-0"
   ```
   Ingress gives an HTTPS address (TLS 1.2 or 1.3), and HTTP on port 80 is
   redirected to HTTPS by default [17]. Port 8080 is the port the .NET
   container image listens on; **verify** it on the published image
   (`ContainerPort` is inferred from `ASPNETCORE_HTTP_PORTS` [2]).
7. **Health probes**: set the liveness and readiness probes to HTTP `GET
   /health` on port 8080 [18] (`/health` is anonymous and not rate limited).
8. **Checks** (below). Record the results in an issue as status codes only.

### Manual checks afterwards

With `<host>` the app's address:

| Check | Expected |
| --- | --- |
| `curl -i https://<host>/health` | `200`, `Healthy` |
| `curl -i https://<host>/api/v1/vehicles/X998ZG` (no credentials) | `401 Unauthorized`, `WWW-Authenticate: Bearer` |
| Same, with a valid ID token of the owner | `200` with vehicle data |
| Same, with a token of another Google account | `403 Forbidden` |
| `az containerapp show ... --query properties.template.scale` | `minReplicas` 0, `maxReplicas` 1 |
| Logging checks 1-4 above | no HTTP logs, no plate in logs, 30 days retention |
| Budget | exists, with the owner's email address |
| Subscription > Access control (IAM) > Role assignments | one Owner (the owner's personal account), no other assignments |
| Microsoft account security page | two-step verification is on |
| Off switch: `az containerapp ingress disable ...`, then `curl -i https://<host>/health` | no `200` from the app; record what is returned and after how long |
| Turn back on: `az containerapp ingress enable ...`, then the four checks in "Turning the app back on" | `/health` `200`; ingress external, target port 8080, insecure off; `minReplicas` 0, `maxReplicas` 1; no credentials gives `401` |
| Rollback: `az containerapp update ... --image <previous tag> --min-replicas 0 --max-replicas 1`, then the first four rows | same results as above; `az containerapp show` shows the previous tag |

The off switch and rollback checks are done once at the first deployment, so
the owner has used both before they are needed.

## Owner decisions

| # | Question | Decision |
| --- | --- | --- |
| Q1 | Image registry? | GHCR, public package. |
| Q2 | Should CI publish an image on every push to `main`? | Yes, publish only, no automatic deployment. The `.github` change is a separate PR. The first deployment may be done by hand. |
| Q3 | Key Vault now? | No. Container Apps secrets for the allow-list; Key Vault when the first real secret (for example the RDW app token) is added. |
| Q4 | Region? | West Europe (`westeurope`), physical location the Netherlands, geography Europe, paired region North Europe (Ireland) [29]. |
| Q5 | Budget amount and alerts? | 10 euro per month; actual cost at 50/80/100%, forecast at 100%. |
| Q6 | Store logs? | Log Analytics, 30 days, immediate purge; no HTTP logs. |
| Q7 | Accept cold starts? | Yes, minimum 0 replicas. Revisit if it bothers users. |
| Q8 | Custom domain? | No; use the default Container Apps address. |

## Consequences

- One more artifact (the container image) per commit on `main`, once CI is
  changed.
- The owner has a few manual steps per deployment until automatic
  deployment is decided.
- With at most one replica there is no scale-out under load; the rate
  limits keep load low, and the global limit holds except during short
  deployment and maintenance overlaps.
- The image is public. Anything added to the image later must be safe to
  publish.

## What could not be verified

- **Prices above the free grants** for Container Apps, Azure Container
  Registry and Log Analytics: the pricing pages showed placeholders [8][16].
- **Cold start duration** on the Consumption plan.
- **Port 8080** of the published image: inferred from the .NET image
  defaults [2], to be confirmed on the built image.
- **Data protection**: Container Apps documentation says "You need to enable
  data protection for all .NET apps on Azure Container Apps" [4]. The API
  uses no cookies or other data-protection features, and runs as one
  replica, so this is probably not needed. To be checked when building.
- **Google OAuth client set-up for ID tokens** (for example whether redirect
  URIs are needed): part of issue #65.
- **Automatic deployment from GitHub to Azure** (for example with federated
  credentials) was not researched; it is out of scope.
- **Off switch timing and response**: how quickly disabling and
  re-enabling ingress take effect, what a caller receives while ingress is
  disabled, and whether the address stays the same.
- **Starting again after scale to zero**: that re-enabling ingress lets the
  default HTTP rule start a replica again (implied, not stated, by the
  scaling documentation); whether an explicit `minReplicas` of 0 counts as
  "defined" in its warning; whether `revision restart` helps if it does not
  start. The `/health` check after re-enabling shows the first point.
- **`ingress show` output**: exact layout and letter case.
- **Rollback keeps environment variables**: whether `az containerapp update`
  with only `--image` keeps them; the exact field path of the image in
  `revision list` output.
- **`az containerapp stop`** is not in the official CLI reference;
  **`revision deactivate`** in single revision mode was not checked.
- **Mandatory Azure MFA for personal Microsoft accounts**: the enforcement
  page describes Entra tenants only. And whether the creator of a personal
  subscription automatically becomes Owner.
- **Key-fetch failure behavior** is observed in a local experiment with one
  package version, not documented by Microsoft; issue #70 turns it into a
  test. The cold-start delay of the first key download was not measured.
- **Euro cost of the worst case** (one replica running all month): the
  amounts in vCPU-seconds and GiB-seconds are computed, the prices are not
  verified.

## Sources

Accessed 2026-10-04.

1. Microsoft Learn, "Containerize an app with dotnet publish" (updated 2026-05-27): <https://learn.microsoft.com/dotnet/core/containers/sdk-publish>
2. Microsoft Learn, "Containerize a .NET app reference" (updated 2026-08-04): <https://learn.microsoft.com/dotnet/core/containers/publish-configuration>
3. Microsoft Learn, "Containers in Azure Container Apps" (updated 2025-05-15): <https://learn.microsoft.com/azure/container-apps/containers>
4. Microsoft Learn, "Scaling in Azure Container Apps" (updated 2026-05-19): <https://learn.microsoft.com/azure/container-apps/scale-app>
5. Microsoft Learn, "Update and deploy changes in Azure Container Apps" (updated 2025-10-27): <https://learn.microsoft.com/azure/container-apps/revisions>
6. Microsoft Learn, "Manage secrets in Azure Container Apps" (updated 2026-09-04): <https://learn.microsoft.com/azure/container-apps/manage-secrets>
7. GitHub Docs, "GitHub Packages billing": <https://docs.github.com/en/billing/concepts/product-billing/github-packages>
8. Microsoft Azure, "Container Registry pricing": <https://azure.microsoft.com/pricing/details/container-registry/>
9. GitHub Docs, "Working with the Container registry": <https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry>
10. GitHub Docs, "GitHub Actions billing": <https://docs.github.com/en/billing/concepts/product-billing/github-actions>
11. Microsoft Learn, "Monitor logs in Azure Container Apps with Log Analytics" (updated 2026-06-01): <https://learn.microsoft.com/azure/container-apps/log-monitoring>
12. Microsoft Learn, "Log storage and monitoring options in Azure Container Apps" (updated 2026-08-18): <https://learn.microsoft.com/azure/container-apps/log-options>
13. Microsoft Learn, "Manage data retention in a Log Analytics workspace" (updated 2026-09-02): <https://learn.microsoft.com/azure/azure-monitor/logs/data-retention-configure>
14. Microsoft Learn, "Tutorial: Create and manage budgets" (updated 2025-06-26): <https://learn.microsoft.com/azure/cost-management-billing/costs/tutorial-acm-create-budgets>
15. Microsoft Learn, "Billing in Azure Container Apps" (updated 2025-12-09): <https://learn.microsoft.com/azure/container-apps/billing>
16. Microsoft Azure, "Azure Monitor pricing": <https://azure.microsoft.com/pricing/details/monitor/>
17. Microsoft Learn, "Ingress in Azure Container Apps" (updated 2025-05-02): <https://learn.microsoft.com/azure/container-apps/ingress-overview>
18. Microsoft Learn, "Health probes in Azure Container Apps" (updated 2025-11-06): <https://learn.microsoft.com/azure/container-apps/health-probes>
19. Microsoft Learn, "Safe storage of app secrets in development in ASP.NET Core" (updated 2026-05-13): <https://learn.microsoft.com/aspnet/core/security/app-secrets>
20. Microsoft Learn, "ASP.NET Core runtime environments" (updated 2026-05-29): <https://learn.microsoft.com/aspnet/core/fundamentals/environments>
21. Microsoft Learn, Azure CLI reference, "az containerapp": <https://learn.microsoft.com/cli/azure/containerapp>
22. Microsoft Learn, Azure CLI reference, "az containerapp revision": <https://learn.microsoft.com/cli/azure/containerapp/revision>
23. Microsoft Learn, "Plan for mandatory Microsoft Entra multifactor authentication (MFA)" (updated 2026-04-03): <https://learn.microsoft.com/entra/identity/authentication/concept-mandatory-multifactor-authentication>
24. Microsoft Learn, Azure CLI reference, "az containerapp ingress": <https://learn.microsoft.com/cli/azure/containerapp/ingress>
25. Microsoft Support, "How to use two-step verification with your Microsoft account": <https://support.microsoft.com/help/12408>
26. Microsoft Learn, "Best practices for Azure RBAC" (updated 2025-03-30): <https://learn.microsoft.com/azure/role-based-access-control/best-practices>
27. Microsoft Learn, "List Azure role assignments using the Azure portal" (updated 2025-10-15): <https://learn.microsoft.com/azure/role-based-access-control/role-assignments-list-portal>
28. Google for Developers, "Verify the Google ID token on your server side" (last updated 2025-12-22): <https://developers.google.com/identity/gsi/web/guides/verify-google-id-token>
29. Microsoft Learn, "List of Azure regions" (updated 2025-09-23): <https://learn.microsoft.com/azure/reliability/regions-list>
