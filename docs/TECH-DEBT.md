# Tech Debt & Deferred Decisions — FlowTrim Backend

Running log of known issues, warnings and decisions we chose to defer.
Each entry records what it is, why it was deferred, and the agreed next step.
Add a new row whenever something is knowingly left for later; close it when resolved.

Status legend: **OPEN** (not addressed) · **PLANNED** (scheduled) · **RESOLVED** (fixed) · **WONTFIX** (accepted as-is).

---

## TD-001 — Transitive `Microsoft.OpenApi 2.0.0` vulnerability (NU1903)

- **Detected:** 2026-10-14 · EP-00 task #1 (`feature/ep-00-dotnet-solution`)
- **Area:** `FlowTrim.Api`
- **Status:** OPEN (accepted for now — option "defer")
- **What:** The .NET 10 `webapi` template pulls, transitively, `Microsoft.OpenApi 2.0.0`,
  which has a known high-severity advisory (NU1903, GHSA-v5pm-xwqc-g5wc). It is not our
  code and the solution builds and tests pass with the warning present.
- **Why deferred:** Fixing it means touching dependencies, which is outside the scope of
  task #1 (create the layered solution). No OpenAPI/Swagger surface is in use yet.
- **Next step:** Re-evaluate when we wire up Swagger/OpenAPI. Options:
  (a) pin a patched `Microsoft.OpenApi` version via a direct `PackageReference`, or
  (b) drop the transitive package if the template feature that brings it is not needed.
- **Owner decision:** Ander approved deferring on 2026-10-14.
- **Note (2026-10-18, HU-38):** the warning also appears in the CI log; warnings do not fail the
  build, so CI stays green. Still OPEN.

---

<!-- Plantilla para nuevas entradas:

## TD-00X — <título corto>

- **Detected:** <fecha> · <tarea/rama>
- **Area:** <proyecto/archivo>
- **Status:** OPEN | PLANNED | RESOLVED | WONTFIX
- **What:** <descripción>
- **Why deferred:** <razón>
- **Next step:** <acción concreta>
- **Owner decision:** <quién y cuándo>

-->
