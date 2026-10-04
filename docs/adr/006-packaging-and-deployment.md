# ADR-006: Packaging and deployment

- Status: Proposed
- Date: 2026-10-04
- Relates to: ADR-004 (REST API), ADR-005 (access control and security)

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

### A. Container image built by the .NET SDK, without a Dockerfile (recommended)

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

**Decision (proposed): at most one replica.** Minimum 0 (scale to zero),
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
  subscription.
- Azure documentation advises Key Vault references instead of direct secret
  values in production [6]. There is no real secret yet (no RDW app token,
  no client secret), so this ADR proposes Container Apps secrets now and Key
  Vault when the first real secret arrives (open question Q3).
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

## CI (proposal only; `.github` is not changed by this ADR)

Proposed addition to the existing workflow, after owner approval in a
separate PR:

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

Proposal:

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
  troubleshooting [12] (open question Q6).

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
  (proposed: 10 euro, alerts at 50%, 80% and 100% of actual cost, and 100% of
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

## First deployment (owner's personal account only)

Prerequisites: issue #65 (a real Google ID token works locally) is done, and
this ADR is accepted. All values in `<angle brackets>` are filled in by the
owner and never committed.

1. **Subscription**: create an Azure subscription with a personal Microsoft
   account (not the Euromaster account). Create the monthly budget alert
   (Cost control).
2. **Resource group** in the chosen EU region:
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

## Open questions for the owner

| # | Question | Recommended default |
| --- | --- | --- |
| Q1 | Image registry? | GHCR, public package. |
| Q2 | Should CI publish an image on every push to `main` (needs a `.github` change)? | Yes, publish only; deploy by hand. |
| Q3 | Key Vault now? | No. Container Apps secrets for the allow-list; Key Vault when the first real secret (for example the RDW app token) is added. |
| Q4 | Region? | West Europe. Both EU regions satisfy ADR-005; no other difference was checked. |
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
