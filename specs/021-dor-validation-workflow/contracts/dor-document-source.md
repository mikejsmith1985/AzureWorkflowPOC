# Contract: DoR Document Source (seam)

Clarify Q2 / D6. A `source_type`-discriminated seam; ship `inline` + `url`; defer `confluence`/`sharepoint`.

## Interface

```csharp
public interface IDorDocumentSource
{
    // Loads the current DoR document, honoring cache_ttl_minutes (0 = always fresh). Best-effort:
    // on fetch failure returns the cached copy with a logged warning; throws DorDocumentUnavailableException
    // only when no cache exists (→ workflow manual-exit, never review against empty DoR — FR edge case).
    Task<DorDocument> LoadAsync(CancellationToken ct = default);
}

public sealed record DorDocument(string Text, string? Version, DateTimeOffset LoadedAt, string SourceType);
```

## Backends (this feature)

| `source_type` | Behavior |
|---|---|
| `inline` | `Text` = `dor.inline_markdown` from config. `Version` = config hash. Always "fresh". |
| `url` | HTTP GET `dor.source_uri` (authless). `Version` = `ETag`/`Last-Modified`. Cached `cache_ttl_minutes`. |
| `confluence` / `sharepoint` | **Deferred** — additional `IDorDocumentSource` implementations behind this seam. |

## Caching

A per-source in-memory cache keyed by `source_uri`; `LoadedAt + cache_ttl_minutes` gates re-fetch. `cache_ttl=0`
disables caching (always fresh). The loaded `Version` is recorded in the audit for each review so historical
records reference the exact DoR in effect (traceability).

## Injection

`DorReviewExecutor` and `ReplyEvalExecutor` inject `DorDocument.Text` as `{{dor_document}}`. The DoR **criteria
are the document**, not hardcoded (FR-006) — updating the document is the only change needed to change what is
evaluated.
