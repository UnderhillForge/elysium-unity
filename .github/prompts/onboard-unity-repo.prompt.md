---
description: "Use when onboarding to this Unity repository and you want a structured deep-dive map of architecture, systems, and where to start contributing."
name: "Onboard Unity Repo"
argument-hint: "Optional focus area, such as combat, UI, save/load, networking, economy, or build pipeline."
agent: "Unity Codebase Familiarizer"
---
Create an onboarding briefing for this Unity repository.

If an argument is provided, treat it as the priority subsystem and still include a concise whole-repo map.

Follow the repository exploration guidance in [Unity Search Hygiene](../instructions/unity-search-hygiene.instructions.md).

## Required Output

### 1) Snapshot
- One-paragraph summary of the project purpose inferred from repository evidence.
- Top-level folder map with each folder's role.

### 2) Runtime Entry Points
- Scene/bootstrap entry points and where runtime initialization begins.
- Key MonoBehaviours, ScriptableObjects, and service layers that shape gameplay flow.

### 3) Architecture Map
- Group findings by subsystem: gameplay, UI, data/config, networking/services, tooling/editor.
- For each subsystem, include concrete file references and a short responsibility summary.

### 4) Developer Workflow
- How to reason about dependencies (Packages, ProjectSettings, docs).
- Where a new contributor should start for safe, high-impact changes.

### 5) Risks and Unknowns
- Gaps, ambiguities, or places requiring validation.
- Next 5 files to inspect in order.

### 6) 30/60/90 Minute Plan
- 30 min: fastest orientation tasks.
- 60 min: first meaningful trace through one feature.
- 90 min: first candidate small contribution area.

Keep findings evidence-based and avoid speculation.
