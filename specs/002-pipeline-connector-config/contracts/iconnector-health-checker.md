# Contract: IConnectorHealthChecker

**Namespace**: `DBAIAzure.Core.Interfaces`  
**Implementation**: `DBAIAzure.Connectors.ConnectorHealthChecker`  
**Registered as**: Singleton (stateless; relies on `IConnectorConfigRepository` for credentials)

---

## Purpose

Executes functional connectivity tests against all four configured connectors. Each test goes
beyond network reachability — it authenticates with the stored credentials and exercises the
target resource to prove the connector is operational for pipeline use.

Used in two contexts:
1. **Operator-initiated test** (Blazor modal "Test Connection" button) — tests a single connector.
2. **Pre-flight check** (called by `PipelineOrchestrator` and `PhaseHandlerOrchestrator` before
   any run) — tests all four connectors in parallel, blocks the run if any fails.

---

## Method Signatures

```csharp
/// <summary>
/// Runs a functional test against the specified connector using the currently stored
/// credentials. Returns a result within 30 seconds under normal network conditions (SC-003).
/// Never returns cached state — always performs a live round-trip to the external service.
/// </summary>
Task<ConnectorTestResult> TestAsync(ConnectorType type, CancellationToken ct = default);

/// <summary>
/// Runs functional tests against all four connectors in parallel (Task.WhenAll).
/// Returns one result per connector. Total duration is bounded by the slowest single test.
/// Used as the pre-flight gate before every pipeline run (FR-018).
/// </summary>
Task<IReadOnlyList<ConnectorTestResult>> CheckAllAsync(CancellationToken ct = default);
```

---

## Per-Connector Test Specifications

### ServiceNow (`FR-008`)

**Test**: Authenticate with Basic Auth (username + password/token) and call  
`GET /api/now/table/sys_properties?sysparm_limit=1` on the configured instance URL.

**Pass condition**: HTTP 200 returned by ServiceNow with at least one record in the response.

**Fail reasons surfaced**: wrong instance URL (DNS / unreachable), authentication rejected (401),
insufficient permissions (403), instance URL does not point to a ServiceNow instance (unexpected
response shape).

---

### Azure DevOps (`FR-009`)

**Test**: Authenticate with the stored PAT and call the ADO REST API to retrieve the configured
project's properties:  
`GET {OrganizationUrl}/_apis/projects/{ProjectName}?api-version=7.1`

**Pass condition**: HTTP 200 returned with the expected project name in the response body.

**Fail reasons surfaced**: wrong organization URL, PAT expired or invalid (401/203), project name
not found (404), PAT lacks read permission on the project (403).

---

### LLM / Anthropic (`FR-010`)

**Test**: Submit a minimal inference request to the configured model endpoint using the stored API
key and model name:  
`POST {ProviderEndpoint}/v1/messages` — body: `{ model, messages: [{ role: "user", content: "Respond with the word READY." }], max_tokens: 5 }`

**Pass condition**: HTTP 200 returned with a `content` array containing a non-empty text response.

**Fail reasons surfaced**: invalid API key (401), model name not found (404 / model_not_found error),
quota exceeded (429), endpoint unreachable, response shape does not match expected schema.

---

### Microsoft Teams (`FR-011`)

**Test**: POST a labeled Adaptive Card message to the configured webhook URL:
```json
{
  "type": "message",
  "attachments": [{
    "contentType": "application/vnd.microsoft.card.adaptive",
    "content": {
      "type": "AdaptiveCard",
      "version": "1.2",
      "body": [{ "type": "TextBlock", "text": "Pipeline connector test — this message confirms your Teams channel is reachable from the pipeline. No action required." }]
    }
  }]
}
```

**Pass condition**: Teams returns `1` (the Teams webhook accepted-response body) with HTTP 200.

**Fail reasons surfaced**: webhook URL malformed, channel deleted or webhook revoked (4xx from
Teams), Teams endpoint unreachable, unexpected response body.

---

## Pre-flight Blocking Logic

`CheckAllAsync()` is called at the start of every run. The caller (orchestrator) inspects the
returned list:

- All results `IsSuccess == true` → run proceeds.
- Any result `IsSuccess == false` → run is blocked; the orchestrator returns the failed results
  to the caller with a diagnostic list identifying each failing connector and its `Message`.

No pipeline step executes until `CheckAllAsync()` returns all-pass (FR-018).

---

## Invariants

- `TestAsync` and `CheckAllAsync` always perform a live round-trip — never read `LastTestResult`
  from the database.
- After a test completes (pass or fail), the implementation calls
  `IConnectorConfigRepository.UpdateTestResultAsync()` to persist the result for the UI status
  display.
- All four tests in `CheckAllAsync` are launched concurrently via `Task.WhenAll` — no sequential
  ordering.
- A `CancellationToken` timeout (default: 35 seconds) is applied per-test to prevent indefinite
  blocking.

---

## Error Conditions

| Condition | Behavior |
|-----------|----------|
| Connector not configured (no secrets stored) | Returns `ConnectorTestResult { IsSuccess = false, Message = "Connector is not configured — no credentials stored." }` |
| Network timeout | Returns fail result with message "Test timed out after 30 seconds — check network connectivity." |
| `IDataProtector.Unprotect()` fails | Returns fail result with message "Stored credentials could not be decrypted — please re-enter them." |
| Unexpected exception from external service | Returns fail result with message containing the HTTP status and a sanitized error description; raw exception details are logged server-side, never surfaced to UI |
