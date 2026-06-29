# AI Cost Rollup — ADO Analytics view (spec-017 US4)

The app maintains two cumulative numeric fields on each work item; **ADO Analytics rolls them up the
work hierarchy** (Task → User Story → Feature → Initiative → Project). No rollup engine lives in the
app (Framework-First) — this is a saved Analytics query + a Power BI report.

## Fields summed
- `Custom.AIRuntimeCostUSD` — pipeline (product) AI spend attributed to the item.
- `Custom.AIDevCostUSD` — development (coding-agent) AI spend attributed to the item.

## OData (Analytics) query — cost by Feature

```
https://analytics.dev.azure.com/{org}/{project}/_odata/v3.0-preview/WorkItems
  ?$apply=filter(WorkItemType eq 'Feature')
    /compute(
      AIRuntimeCostUSD with sum as RuntimeUsd,
      AIDevCostUSD     with sum as DevUsd)
  &$select=WorkItemId,Title,RuntimeUsd,DevUsd
```

For true descendant rollup (children under each Feature/Initiative), use the **Analytics "Work Items -
Today" hierarchy** with the parent/child link type and aggregate `AIRuntimeCostUSD` + `AIDevCostUSD`
over the descendants. In Power BI:
1. Connect to the project's Analytics OData feed.
2. Load `WorkItems` + the `WorkItemLinks` (hierarchy) entity.
3. Build a parent→descendant rollup measure summing each cost field.
4. Visualize by Initiative / Feature, with Runtime vs Development as separate measures (SC-006).

## Notes
- The two dimensions stay **distinguishable** (separate fields) so "cost to build" (Dev) and "cost to
  run" (Runtime) are answerable independently (SC-006).
- Per-item fields are recomputed from the append-only cost ledger on each append, so they are always
  the current cumulative total (SC-001/SC-003).
- The fields are provisioned by the ADO telemetry preflight (Bootstrap) or mapped to native fallbacks
  (Adaptive); re-run the preflight after deploy to create them.
