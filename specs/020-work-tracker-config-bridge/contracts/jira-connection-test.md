# Contract: Jira connection test (`JiraConnectorTester`)

The Jira "Test Connection" behind the existing `IConnectorHealthChecker` seam — parity with the ADO
preflight-based tester (D5, FR-007, SC-004).

## Seam (existing, reused)

```csharp
// existing: returns ConnectorTestResult(Type, IsSuccess, Message, TestedAt)
Task<ConnectorTestResult> TestAsync(CancellationToken ct = default);
```

The UI resolves the tester matching the **selected provider** and persists the outcome via
`IConnectorConfigRepository.UpdateTestResultAsync(ConnectorType.WorkTracker, result)`.

## `JiraConnectorTester` steps (best-effort, ordered — first failure returns an actionable message)

1. **Credential presence** — SiteUrl / Email / ApiToken / ProjectKey all present, else fail naming what's
   missing.
2. **Auth + reachability** — `GET /rest/api/3/myself`. Non-2xx / HTML sign-in page ⇒ fail ("token invalid or
   expired"). Success confirms the account.
3. **Project existence** — `GET /rest/api/3/project/{projectKey}`. 404 ⇒ fail ("project key not found or no
   access").
4. **Success** — message names the authenticated account + confirmed project. **Creates no issue** (safe probe).

## Contract guarantees

- **No writes**: the test never creates or mutates a Jira issue (SC — safe to run repeatedly).
- **Actionable, sanitized messages**: message states the confirmed fact or the specific failure cause; never
  contains raw exception traces or the token (Article IX; consistent with `ConnectorTestResult` doc).
- **Latency**: returns within a few seconds for reachable sites (SC-004).
- **Result invalidation**: editing any field resets `LastTestResult` (existing `SaveAsync` behavior, FR-017).
