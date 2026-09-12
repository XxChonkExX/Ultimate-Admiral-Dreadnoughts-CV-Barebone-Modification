# UADCarrier — Hull Unlocks (Option B)

## Why not a new tech line?

The game ignores unknown tech **groups** and **types** from CSV — new `aviation`
entries never appear in the research UI, even with correct structure, localization,
and no prerequisites (verified by testing). Adding a dedicated Aviation tech line
requires a Harmony patch to the tech registry (code, not data).

For a data-only mod others can easily modify, carrier hulls are instead unlocked
through **existing** hull technologies.

## Current unlock mapping

| Hull | Unlocked by (existing tech) | Era |
|---|---|---|
| `cv_1_langley` (Experimental Carrier) | `hull_strength_12` (1919) | 1920s cruiser conversions (small hull, Mk 1 wings) |
| `cv_2_yorktown` (Fleet Carrier) | `hull_strength_14` (1929) | 1930s purpose-built (medium hull, Mk 2 wings) |
| `cv_3_essex` (Heavy Fleet Carrier) | `hull_strength_end` (1940) AND `hull_strength_14` (1929, backup) | 1940s dedicated fleet hull (large, Mk 3 wings — Essex-class analogues) |

> Findings: (1) `hull_strength_end` never fires its `unlock()` (an `end`-tech
> quirk) — the 1929 entry is the live one; the 1940 entry is kept for
> documentation/future-proofing. (2) The designer hull grid hides hulls whose
> `tonnageMin` exceeds the current derived displacement (no visible control).
> Essex lists because its floor is 14,000t (Yorktown's); it still grows to
> 36kt. Keep big-hull `tonnageMin` ≤ 20kt or they vanish from the grid.

Each is an `unlock(cv_*;…)` entry appended inside that tech's existing `unlock(...)`
list in `technologies.csv`. To retarget a hull to a different era, move its
`cv_*` name into another tech's `unlock(...)` list.

## Design rule: no year gating on carrier content

Carrier hulls, tubes, and the CV type carry **no year restrictions of their own**
(no year columns, no `obsolete()` entries). Availability is purely research-driven:
whoever researches the unlocking `hull_strength` tech first gets carriers first,
regardless of campaign year. Rushing naval research to field carriers early is
intended emergent play, not an exploit.

## What was removed

Earlier prototypes added `techGroups_carrier.csv`, `techTypes_carrier.csv`, and
`technologies_carrier.csv` with a new `aviation` line. Those files have been
deleted — the game silently ignores them. The English.lng aviation strings
(`$techGroup_name_aviation`, etc.) remain registered but unused; they are harmless
and reserved for a future code-based tech line.

## Future (code) path

If someone implements the Harmony tech-registry patch, the deleted overlays can be
restored from git history and the `unlock(cv_*)` entries moved from the
`hull_strength_*` techs onto dedicated `aviation_*` technologies.
