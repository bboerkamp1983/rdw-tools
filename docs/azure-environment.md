# Azure base environment

- Set up: 2026-10-06, with Azure CLI 2.91.0
- Relates to: ADR-005 (hosting), ADR-006 ("First deployment", steps 1-4)

This note records what exists in Azure before the first deployment, so it can
be checked and rebuilt. It contains no secrets and no subscription, tenant or
workspace IDs.

## What exists

| Item | Name / value |
| --- | --- |
| Account | The owner's personal Microsoft account, two-step verification on. Not the Euromaster account. |
| Subscription | `Azure subscription 1`, pay-as-you-go (the account had used its free account before). Directory: the account's own default directory. |
| Budget | On the subscription: 10 euro per month; email alerts at 50%, 80% and 100% of actual cost and 100% of forecast (ADR-006 Q5). Created in the portal. |
| Region | **North Europe** (`northeurope`), Ireland [1]. See "Region change". |
| Resource group | `rg-rdw-tools`, `northeurope` |
| Log Analytics workspace | `log-rdw-tools`, `northeurope`; SKU `PerGB2018`; retention 30 days; `immediatePurgeDataOn30Days` `true` [2]; daily cap 0.1 GB |
| Container Apps environment | `cae-rdw-tools`, `northeurope`; workload profiles environment with only the `Consumption` profile; logs destination `log-analytics` to `log-rdw-tools` [3]; no custom virtual network |
| Diagnostic settings | None, on the environment and on the workspace |
| HTTP logs | Not enabled |
| Resource providers | `Microsoft.OperationalInsights` and `Microsoft.App`, registered automatically by the CLI |

Not created yet: the container app, the image, and any secrets.

## Region change

ADR-006 chose West Europe (Q4). Creating the workspace there failed with
`RequestDisallowedByAzure` / `LocationIneligible`: the region did not accept
this new subscription. Microsoft documents this: "To prioritize resources for
existing customers in an Azure region, Microsoft may restrict access for
customers without resources in that location", and the policy "is currently
in effect" for West Europe [11]. Its advice is that "Most users should select
a different Azure region"; a support request is meant only for tenants that
already have resources there or a clear need for country-specific data
sovereignty [11], neither of which applies. The owner chose North Europe instead, which ADR-005 allows ("West
Europe or North Europe"). North Europe is in Ireland, an EU member state,
geography Europe, paired with West Europe [1].

The resource group was first created in West Europe (that worked; it holds
only metadata [4]), then deleted while still empty and created again in North
Europe, so the group and its resources share one region as Microsoft
recommends [4].

## Daily cap

The owner added a daily ingestion cap of 0.1 GB to the workspace (not in
ADR-006). Unlike the budget, the cap stops ingestion when reached; the app
keeps running, but logs for the rest of that day are lost. The cap resets
daily at 10:00 UTC (the workspace's `quotaNextResetTime`). The CLI minimum is
0.023 GB and the default is unlimited (`-1`) [5].

## Who ran what

The owner ran the commands and portal steps, except:

- `az containerapp env create` (the environment) and
  `az monitor log-analytics workspace update --quota 0.1` (the daily cap) were
  run by Claude, at the owner's explicit request, through the owner's signed-in
  local Azure CLI session;
- Claude also ran read-only checks (`show`, `list`) the same way.

At the time, ADR-006 said "Claude and other AI agents get no Azure access;
they do not run `az` commands." No role assignment or credential was given to
Claude; it used the owner's local session. Afterwards the owner amended
ADR-006 (Q9): Claude may run `az` commands through the owner's local session,
but only after asking for and receiving explicit approval for each action.

## How it was created

```powershell
az group create --name rg-rdw-tools --location northeurope

az monitor log-analytics workspace create --resource-group rg-rdw-tools --workspace-name log-rdw-tools --location northeurope --retention-time 30 --sku PerGB2018
az resource update --resource-group rg-rdw-tools --name log-rdw-tools --resource-type Microsoft.OperationalInsights/workspaces --set properties.features.immediatePurgeDataOn30Days=true --output none
az monitor log-analytics workspace update --resource-group rg-rdw-tools --workspace-name log-rdw-tools --quota 0.1

az containerapp env create --name cae-rdw-tools --resource-group rg-rdw-tools --location northeurope --logs-destination log-analytics --logs-workspace-id <workspace customerId>
```

- `immediatePurgeDataOn30Days` cannot be set in the portal; Microsoft
  documents it as a REST `PATCH` [2]. `az resource update` sets the same
  property; the check below shows it took effect.
- Only the workspace ID was passed to `az containerapp env create`, as in
  Microsoft's example [3]. The CLI found the workspace key itself; no key was
  handled by hand. Always pass the workspace: without it the CLI creates a new
  one.

## Checks

Expected results as of 2026-10-06.

```powershell
az resource show --resource-group rg-rdw-tools --name log-rdw-tools --resource-type Microsoft.OperationalInsights/workspaces --query "{location:location,sku:properties.sku.name,retention:properties.retentionInDays,purge30:properties.features.immediatePurgeDataOn30Days,dailyQuotaGb:properties.workspaceCapping.dailyQuotaGb}" --output table
```

`northeurope`, `PerGB2018`, `30`, `True`, `0.1`.

```powershell
az containerapp env show --name cae-rdw-tools --resource-group rg-rdw-tools --query "{location:location,state:properties.provisioningState,logs:properties.appLogsConfiguration,profiles:properties.workloadProfiles}" --output json
```

`North Europe`, `Succeeded`, destination `log-analytics` with the customerId
of `log-rdw-tools`, one workload profile `Consumption`.

```powershell
$envId = az containerapp env show --name cae-rdw-tools --resource-group rg-rdw-tools --query id --output tsv
az monitor diagnostic-settings list --resource $envId --output table
```

No output: no diagnostic settings.

## Cost

- Resource group, budget and an environment without apps cost nothing. The
  environment has no Dedicated profile, so no plan management fee [6].
- The workspace is billed per GB ingested [7]; the daily cap limits this.
- A budget does not stop spending [8]. For pay-as-you-go subscriptions, cost
  data can take up to 72 hours to appear [9], longer than the 8-24 hours in
  ADR-006.

## Teardown

**This cannot be undone.** It deletes the resource group with everything in
it, including all logs. Check first with `az account show` that the personal
subscription is active.

Optional, to remove the logs at once instead of after a 14-day soft delete
[10]:

```powershell
az monitor log-analytics workspace delete --resource-group rg-rdw-tools --workspace-name log-rdw-tools --force
```

Then:

```powershell
az group delete --name rg-rdw-tools
az group exists --name rg-rdw-tools
```

The second command returns `false` when the group is gone. The subscription
and the budget remain.

## Next

ADR-006 "First deployment", steps 5-8: the image, the container app (minimum
0 and maximum 1 replica), health probes and the manual checks. Before the app
goes live, confirm that request URLs (which contain the plate) do not reach
the console logs, which are stored in `log-rdw-tools` (ADR-006 logging checks).

## Sources

Accessed 2026-10-06.

1. Microsoft Learn, "List of Azure regions": <https://learn.microsoft.com/azure/reliability/regions-list>
2. Microsoft Learn, "Manage data retention in a Log Analytics workspace": <https://learn.microsoft.com/azure/azure-monitor/logs/data-retention-configure>
3. Microsoft Learn, "Log storage and monitoring options in Azure Container Apps": <https://learn.microsoft.com/azure/container-apps/log-options>
4. Microsoft Learn, "What is Azure Resource Manager?": <https://learn.microsoft.com/azure/azure-resource-manager/management/overview>
5. Microsoft Learn, Azure CLI reference, "az monitor log-analytics workspace": <https://learn.microsoft.com/cli/azure/monitor/log-analytics/workspace>
6. Microsoft Learn, "Billing in Azure Container Apps": <https://learn.microsoft.com/azure/container-apps/billing>
7. Microsoft Azure, "Azure Monitor pricing": <https://azure.microsoft.com/pricing/details/monitor/>
8. Microsoft Learn, "Tutorial: Create and manage budgets": <https://learn.microsoft.com/azure/cost-management-billing/costs/tutorial-acm-create-budgets>
9. Microsoft Learn, "Understand Cost Management data": <https://learn.microsoft.com/azure/cost-management-billing/costs/understand-cost-mgt-data>
10. Microsoft Learn, "Delete and recover a Log Analytics workspace": <https://learn.microsoft.com/azure/azure-monitor/logs/delete-workspace>
11. Microsoft Learn, "Resolve location ineligible errors" (updated 2025-05-07, accessed 2026-10-06): <https://learn.microsoft.com/azure/azure-resource-manager/troubleshooting/error-region-access-policy>
