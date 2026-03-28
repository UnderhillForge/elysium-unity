# Elysium Planned API Reference

Status: Planned and Future-Facing
Last Updated: March 2026

## Scope

This document captures APIs and interfaces that are not yet implemented in code.

Included:
- Future backend and web APIs
- Future Lua/script capabilities
- Future runtime and platform expansions

Excluded:
- Implemented contracts already documented in [docs/elysium_api.md](elysium_api.md)

## Promotion Policy

A planned API is promoted into the implemented contract document only when all are true:
- Code implementation exists in the repository.
- Public signature and field names are finalized.
- Documentation block exactly matches the implemented contract.

## Planned Server APIs

### Session Management (WebSocket)

Planned endpoint shape:
- Session creation and lifecycle orchestration for multiplayer hosting
- GM policy handoff and player connection metadata

### Lobby API (REST)

Planned endpoint shape:
- Lobby discovery and join operations
- Session routing metadata and access token provisioning

### World Package Registry API (REST)

Planned endpoint shape:
- Package browse/search and compatibility filtering
- Package metadata, dependency, and popularity surfaces

### Account and Population APIs (REST)

Planned endpoint shape:
- Account profile and campaign telemetry
- Population snapshots and aggregate activity metrics

## Planned Gameplay and Lua Expansions

### Pathfinder Rules Expansion

Planned:
- Extended feat/stat resolution APIs
- Additional combat resolver and rules query surfaces
- Expanded script callable rules helpers beyond current contract

### Advanced World Scripting

Planned:
- Waypoint and behavior state-machine helpers
- Event broadcast and richer world state APIs
- Additional controlled world mutation surfaces

## Planned Platform Expansions

### Web Frontend Integration

Planned:
- Hosted campaign lobbies and package browser workflows
- Character builder and package publication UX contracts

### Dedicated Server Mode

Planned:
- Dedicated server session topology operations
- Sharding and crossover support surfaces
- Server-side account and session persistence boundaries

## Planned Versioning Notes

- Planned APIs may change without backward compatibility guarantees.
- Stable compatibility rules apply only after APIs are promoted into implemented contract docs.

## Candidate Migration Queue

Use this section as a staging index for APIs ready to move into [docs/elysium_api.md](elysium_api.md):
- Add entries here when implementation PRs merge.
- Remove entries after implemented contract blocks are updated.
