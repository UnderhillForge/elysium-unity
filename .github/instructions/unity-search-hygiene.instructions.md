---
description: "Use when exploring, auditing, or mapping this Unity codebase to keep searches fast and signal-rich."
name: "Unity Search Hygiene"
---
# Unity Search Hygiene

Use these rules whenever analyzing this repository.

## Focus Areas First
- Start in `Assets/`, `Packages/`, `ProjectSettings/`, and `docs/`.
- Prioritize gameplay and app logic under `Assets/` before inspecting package internals.
- Use `ProjectSettings/` and `Packages/manifest.json` to understand configuration and dependency context.

## De-prioritize Generated Folders
- Skip `Library/`, `Temp/`, and `Logs/` by default.
- Only inspect generated folders when debugging import issues, build pipeline behavior, or editor cache anomalies.

## Search and Reading Strategy
- Begin with narrow, intent-based searches (feature names, scene names, MonoBehaviour class names, ScriptableObject types).
- Expand scope gradually: feature entry points, then service/util layers, then package internals.
- Prefer tracing end-to-end flow: scene or bootstrap file, runtime component, data/config, side effects.

## Evidence Standard
- Ground all claims in concrete repository evidence.
- If uncertain, state the gap and name the next files to inspect.
