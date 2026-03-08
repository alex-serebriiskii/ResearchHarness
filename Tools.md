# Tools

PowerShell scripts in `tools/` that encapsulate the common agent operations needed during development and live testing of ResearchHarness. Call them from the project root with:

```
powershell -ExecutionPolicy Bypass -File tools\<script>.ps1 [params]
```

The `-ExecutionPolicy Bypass` flag is required because Windows blocks unsigned local scripts by default. All examples in this file assume it.

---

## Classification: tools vs skills

**Tools** are executable scripts — they _do_ something (start a process, submit a request, wait for a result). An agent reaches for a tool when it needs to take an action against the running system.

**Skills** are knowledge packs — they _teach_ something (architecture, debugging patterns, API contracts). An agent reads a skill to understand the codebase before acting on it.

---

## App lifecycle

### `tools\start-app.ps1`

Stops any running instance, optionally rebuilds, starts the web app in the background with stdout/stderr redirected to `run.log` / `run.err`, and polls the health endpoint until the app is ready.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `-Port` | int | 5000 | Listening port |
| `-NoBuild` | switch | — | Skip `dotnet build` before starting |
| `-LogFile` | string | `run.log` | Path for stdout redirect |
| `-ErrFile` | string | `run.err` | Path for stderr redirect |

Exits 0 when the app is accepting connections; exits 1 on build failure or 30-second readiness timeout.

```
powershell -File tools\start-app.ps1
powershell -File tools\start-app.ps1 -NoBuild
powershell -File tools\start-app.ps1 -Port 5001
```

### `tools\stop-app.ps1`

Kills the running `ResearchHarness.Web` process. No-ops silently if the process is not found.

```
powershell -File tools\stop-app.ps1
```

---

## Research pipeline operations

### `tools\submit-job.ps1`

POSTs a new research job and prints the returned job ID to stdout.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `-Theme` | string | **required** | Research query |
| `-Port` | int | 5000 | App port |

```
powershell -File tools\submit-job.ps1 -Theme "Identify the top 3 most successful stocks of the last decade"
```

Capture the output to chain into subsequent commands:
```powershell
$jobId = powershell -File tools\submit-job.ps1 -Theme "Your query"
```

### `tools\poll-job.ps1`

Polls job status on an interval until the job reaches `Completed` or `Failed`. Prints a timestamped status line each interval. Exits 0 for Completed, 1 for Failed or timeout.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `-JobId` | string | **required** | Job ID from submit-job |
| `-Port` | int | 5000 | App port |
| `-IntervalSeconds` | int | 10 | Poll interval |
| `-TimeoutSeconds` | int | 1800 | Max wait (30 minutes) |

```
powershell -File tools\poll-job.ps1 -JobId "3fa85f64-..."
```

### `tools\show-journal.ps1`

Fetches the completed journal and prints it in human-readable sections: overall summary, cross-topic analysis, per-paper executive summary + findings, bibliography.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `-JobId` | string | **required** | Job ID |
| `-Port` | int | 5000 | App port |

```
powershell -File tools\show-journal.ps1 -JobId "3fa85f64-..."
```

---

## Development operations

### `tools\build.ps1`

Runs `dotnet build` across the solution. Use `-StopApp` to kill the running process first (required on Windows due to DLL locking).

| Parameter | Type | Default | Description |
|---|---|---|---|
| `-Configuration` | string | `Debug` | Build configuration |
| `-StopApp` | switch | — | Kill app process before building |

```
powershell -File tools\build.ps1 -StopApp
powershell -File tools\build.ps1 -Configuration Release
```

### `tools\run-tests.ps1`

Builds the unit test project and runs all 51 tests via TUnit on Microsoft.Testing.Platform.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `-NoBuild` | switch | — | Skip build step |
| `-Configuration` | string | `Debug` | Build configuration |

```
powershell -File tools\run-tests.ps1
powershell -File tools\run-tests.ps1 -NoBuild
```

**Note:** TUnit does not support `--nologo`. The script does not pass it.

---

## Typical agent workflow

```powershell
# 1. Stop app if running (needed before rebuild on Windows)
powershell -File tools\stop-app.ps1

# 2. Build
powershell -File tools\build.ps1

# 3. Run tests
powershell -File tools\run-tests.ps1

# 4. Start app
powershell -File tools\start-app.ps1 -NoBuild

# 5. Submit job and capture ID
$jobId = powershell -File tools\submit-job.ps1 -Theme "Your research query"

# 6. Wait for completion
powershell -File tools\poll-job.ps1 -JobId $jobId

# 7. Read results
powershell -File tools\show-journal.ps1 -JobId $jobId
```
