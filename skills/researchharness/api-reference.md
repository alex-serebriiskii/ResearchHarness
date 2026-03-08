# ResearchHarness HTTP API Reference

All `/internal/` routes require the `X-Internal-Key` header. The value must match `InternalApi:Key` from config. Requests missing or presenting an incorrect key are rejected by `ApiKeyMiddleware` with HTTP 401.

Base URL (local): `http://localhost:5000`

---

## POST /internal/research/start

Enqueue a new research job.

**Request headers:**
```
X-Internal-Key: <key>
Content-Type: application/json
```

**Request body:**
```json
{ "theme": "string" }
```

**Response:** HTTP 200, body is a quoted GUID string.

```
"3fa85f64-5717-4562-b3fc-2c963f66afa6"
```

**Curl example:**
```bash
curl -s -X POST http://localhost:5000/internal/research/start \
  -H "X-Internal-Key: YOUR_KEY" \
  -H "Content-Type: application/json" \
  -d '{"theme": "impact of large language models on scientific publishing"}'
```

---

## GET /internal/research/{jobId}/status

Poll the current status of a job.

**Request headers:**
```
X-Internal-Key: <key>
```

**Path parameter:** `jobId` — GUID returned by the start endpoint.

**Response:** HTTP 200, body is a JSON string enum.

Possible values:

| Value | Meaning |
|---|---|
| `"Pending"` | Accepted, not yet started |
| `"Researching"` | Lab agents executing search tasks |
| `"Synthesizing"` | PI agents writing papers |
| `"Reviewing"` | Reserved (not used in Phase 1) |
| `"Assembling"` | Lead agent assembling journal |
| `"Completed"` | Journal ready |
| `"Failed"` | Unrecoverable error |

**Curl example:**
```bash
curl -s http://localhost:5000/internal/research/3fa85f64-5717-4562-b3fc-2c963f66afa6/status \
  -H "X-Internal-Key: YOUR_KEY"
```

---

## GET /internal/research/{jobId}/journal

Retrieve the assembled journal for a completed job.

**Request headers:**
```
X-Internal-Key: <key>
```

**Path parameter:** `jobId` — GUID returned by the start endpoint.

**Response:** HTTP 200, full Journal JSON object.

**Journal schema:**

```
{
  "overallSummary": string,
  "crossTopicAnalysis": string,
  "assembledAt": string (ISO 8601 datetime),
  "papers": [
    {
      "topicId": string (UUID),
      "executiveSummary": string,
      "confidenceScore": number (0.0 - 1.0),
      "revisionCount": int,
      "findings": [
        {
          "subTopic": string,
          "summary": string,
          "keyPoints": string[],
          "sourceUrl": string,
          "relevanceScore": number (0.0 - 1.0)
        }
      ],
      "bibliography": [
        {
          "url": string,
          "title": string,
          "credibility": "High" | "Medium" | "Low" | "Unknown"
        }
      ],
      "reviews": []
    }
  ],
  "masterBibliography": [
    {
      "url": string,
      "title": string,
      "credibility": "High" | "Medium" | "Low" | "Unknown",
      "credibilityRationale": string
    }
  ]
}
```

**Curl example:**
```bash
curl -s http://localhost:5000/internal/research/3fa85f64-5717-4562-b3fc-2c963f66afa6/journal \
  -H "X-Internal-Key: YOUR_KEY" | python -m json.tool
```
