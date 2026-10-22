# Plan: Shader dissipation for corpses

## Goal
Replace the alpha fade-out of dying corpses with a per-pixel dissolve that scatters pixels outward like particles.

## Context
- `src/Entities/Corpse.cs` exposes `AlphaFactor` used in rendering at line ~1226 to tint corpse alpha.
- Dissipation should start during `FadeDuration` (1.5s) before `Lifetime`.

## Approach
1. Add a dissolve progress value `Dissolve = 1 - AlphaFactor` (0..1).
2. In renderer (`EntityRendering.cs` corpse loop), instead of tint alpha, sample a noise threshold: discard pixels where noise < Dissolve, and offset surviving pixels by a small vector derived from (noise, angle) to simulate lift.
3. Implement as a custom Raylib shader or CPU-side pixel manipulation; prefer a fragment shader if shader pipeline exists.
4. Keep physics until corpse removed.

## Risks
- Shader pipeline may not exist; fallback CPU dissolve costly.
- Need ensure networked corpses sync dissolve via Age/Lifetime (already synced through dto? verify).

## Validation
- Build succeeds.
- Visual check in-game.