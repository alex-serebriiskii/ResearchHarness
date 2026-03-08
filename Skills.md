# Skills

OMP-compatible skill knowledge packs in `skills/`. Each skill is a directory containing a `SKILL.md` entry point with YAML frontmatter and supporting reference documents. Agents load skills on demand via `skill://<name>` URLs.

---

## Classification: tools vs skills

**Skills** are passive knowledge — architecture diagrams, debugging patterns, API contracts, gotchas. An agent reads a skill to understand the system before acting on it.

**Tools** are active scripts — they start processes, submit requests, wait for results. See `Tools.md`.

---

## Discovery

Skills follow the OMP layout: `skills/<skill-name>/SKILL.md`. The runtime discovers them one level under `skills/` (non-recursive). Each `SKILL.md` carries `name` and `description` frontmatter required for discovery.

Reference a skill in any agent session:
```
skill://researchharness          # loads SKILL.md
skill://researchharness/debugging.md
skill://researchharness/api-reference.md
```

---

## Available skills

### `researchharness`

> Architecture, development workflow, and debugging guide for the ResearchHarness .NET 10 agentic research pipeline

**Entry point:** `skills/researchharness/SKILL.md`

Covers:
1. Pipeline overview — agent hierarchy, data flow from theme → journal
2. Solution structure — six projects and their responsibilities
3. Key files — which files to read before touching any layer
4. Development workflow — exact commands for build, test, start, submit, poll, display
5. LLM integration rules — the DTO nullability and `SnakeCaseLower` naming policy contract that must not be broken
6. Configuration reference — `appsettings.json` shape with field explanations
7. Links to supporting docs

**Supporting documents:**

| File | Purpose |
|---|---|
| `skills/researchharness/debugging.md` | Runtime failure patterns, log signatures, and exact mitigations |
| `skills/researchharness/api-reference.md` | HTTP endpoint reference with curl examples and the Journal JSON schema |

**Load this skill when:**
- Starting work on any part of the ResearchHarness codebase
- Debugging a failed pipeline run
- Modifying LLM DTOs, prompt factories, or the LLM client
- Adding a new pipeline stage or agent
- Configuring models or understanding cost/reliability tradeoffs

---

## Adding a new skill

1. Create `skills/<skill-name>/SKILL.md` with frontmatter:
   ```yaml
   ---
   name: <skill-name>
   description: One sentence — what this skill teaches and when to load it
   alwaysApply: false
   ---
   ```
2. Write the skill body — prefer sections over prose, include exact file paths and command examples
3. Put supporting reference files in the same directory; link from SKILL.md with `skill://<skill-name>/<file>`
4. Add an entry to this file under "Available skills"
5. Do not nest skills (`skills/group/skill/SKILL.md` is not discovered by the runtime)
