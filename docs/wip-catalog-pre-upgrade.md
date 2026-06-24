# WIP Catalog — pre-upgrade snapshot (2026-06-23)

A snapshot of every uncommitted change in the working tree as of the decision
to upgrade this fork to upstream `RimWorld-Together/Rimworld-Together`
`26.6.23.1`. Each entry records:

- **Intent** — what the change was meant to do
- **Surface** — files + lines touched
- **Wire effect** — whether the change alters network packet shape
- **Re-eval after upgrade** — what to check / how to decide if it still applies
- **Re-application path** — if we still want it, how to do it on the new base

After the upgrade lands, walk every entry. For each: keep / drop / re-apply.

---

## Fork foundation context (what is this catalog measured against?)

| Thing | Value |
|---|---|
| Fork base commit (HEAD) | `44b909b6 Add local-only paths to .gitignore` |
| Merge-base with upstream `26.6.23.1` | `d317be59 Dll modifications` |
| Fork's `RTShared.dll` version constant | `26.5.24.1` |
| Fork's `RTNetwork.dll` adds | `RTNetwork.ServerClient` type (not in upstream) |
| Upstream `26.6.23.1` removed | `Source/Client/` tree (`ce65a8a7`); `Source/Assemblies/Shared/` folder (relocated to `Source/Assemblies/`) |

The fork-vs-upstream divergence is **ABI-breaking**: PKT_* property names in
`RTShared.dll` were renamed PascalCase upstream (e.g. `_stepMode` ->
`StepMode`); `PKT_Map` was restructured to carry raw `byte[]` instead of
`FL_Map`. So the entries below — which target the fork's `Source/Client/` and
the fork's underscore-property convention — are not source-cherry-pickable
onto upstream. They either need to be re-implemented against the new API or
expressed as Harmony patches on top of upstream's DLL.

---

## Change 1 — Crash hardening: MainThreadHandler dispatcher try/catch

**Intent:** Prevent any single thrown packet-handler from tearing down the
client TCP connection. Symptom that prompted it: NREs visible in player logs
from the 2026-06-17 sessions causing visit sessions to drop mid-stream.

**Surface:**
- `Source/Client/Misc/MainThreadHandler.cs:93-107` — `ExecuteAllQueue` wraps
  `ActionQueue.Dequeue().Invoke()` in try/catch logging via `Printer.Error`.
  `ActionWrapper` coroutine wraps `action()` in try/catch the same way.
- 6 net lines added, 2 lines changed.

**Wire effect:** None. Pure local error handling.

**Re-eval after upgrade:**
- Read upstream's `Source/Assemblies/RTClient.dll` via dnSpy and check the
  decompiled `MainThreadHandler.ExecuteAllQueue` and `ActionWrapper`. If
  upstream already has try/catch in both, drop this entry — done for us.
- If upstream's version still rethrows, we want this fix on the new base.

**Re-application path (if upstream doesn't have it):**
- Option A: write a small Harmony patch in a new `RTMP_Patches` mod assembly
  that prefixes `MainThreadHandler.ExecuteAllQueue` and the `ActionWrapper`
  coroutine with try/catch wrappers. (~30 lines.)
- Option B: ship the fork's `Source/Client/` alongside upstream — but this
  requires our client source to compile against the new RTShared.dll, which
  it currently does not. Large effort.
- Recommended: Option A.

---

## Change 2 — Crash hardening: ServerNetwork OnDisconnect null guard

**Intent:** Prevent NRE when an `OnDisconnect` fires before the client has
authenticated and had an `FL_Player` attached to it. Without the guard,
`client.GetData<FL_Player>().Username` blows up and the disconnect handler
exits before `Network.ServerClients.Remove` / `SendPlayerRecount` run,
leaking the client slot.

**Surface:**
- `Source/Server/Hooks/TCPNetwork/ServerNetwork.cs:25-36` — null-check
  `client.GetData<FL_Player>()` before accessing `.Username` in the
  `DisconnectNotifications` branch.
- 5 net lines added, 1 line changed.

**Wire effect:** None. Pure server-side defensive programming.

**Re-eval after upgrade:**
- Check `Source/Server/Hooks/TCPNetwork/ServerNetwork.cs` on upstream HEAD.
  Upstream's `27a1d7da "Namespace fixes"` touched this file but only removed
  a `using RTNetwork;` line. The `OnDisconnect` body should still be the same
  unsafe shape. **Likely still needed.**

**Re-application path (if still needed):**
- Direct apply: this is server source, both we and upstream maintain it as
  source. Apply the same diff to the upstream version of the file. ~5 minutes.

---

## Change 3 — Compress-flag refactor (SynchronousOptions constant)

**Intent:** Replace the hard-coded `false` literal in 14+ Serializer call
sites across the Synchronous packet managers with a single named constant.
Goal: enable F1 (flip the second-arg bool on Serializer to `true` for
Synchronous packets — believed to be a compression toggle, but the bool's
exact semantic is NOT confirmed from source) to be a one-line change instead
of touching every site. **Secondary win:** the refactor caught and fixed a
sender/receiver mismatch — `PM_SGameSpeed.Handle` was calling
`ConvertBytesToObject(data.Contents)` (default-arg) while the sender used
explicit `false`. Whatever the bool means, having the two sides disagree is
unambiguously a bug.

**Surface:**
- NEW: `Source/Client/Misc/SynchronousOptions.cs` — 22 lines, `public const
  bool CompressContents = false;`
- Modified, replacing literal `false` with `SynchronousOptions.CompressContents`:
  - `Source/Client/PacketManagers/Synchronous/PM_SDestroy.cs` (2 sites)
  - `Source/Client/PacketManagers/Synchronous/PM_SDraft.cs` (2 sites)
  - `Source/Client/PacketManagers/Synchronous/PM_SGameSpeed.cs` (3 sites — one is the bug fix)
  - `Source/Client/PacketManagers/Synchronous/PM_SHediff.cs` (2 sites)
  - `Source/Client/PacketManagers/Synchronous/PM_SJob.cs` (2 sites)
  - `Source/Client/PacketManagers/Synchronous/PM_SMentalState.cs` (2 sites)
  - `Source/Client/PacketManagers/Synchronous/PM_SWeather.cs` (2 sites)
  - `Source/Client/PacketManagers/Synchronous/PM_Synchronous.cs` (2 sites)

**Wire effect:** Currently NONE — the constant evaluates to `false`, which is
exactly what the old hard-coded value was. **But the value is the entire
point of the refactor**: flipping it to `true` is F1, which IS wire-changing.

**Re-eval after upgrade:**
- Upstream's `Source/Client/` doesn't exist, so these source files have no
  upstream counterpart. The PM_S* logic now lives in upstream's compiled
  `RTClient.dll`. Decompile and check what compression value upstream uses
  for Synchronous packets — they may have already flipped the bit.
- The PM_SGameSpeed sender/receiver mismatch fix: check upstream's decompiled
  version. If they fixed it too, drop the bug-fix portion.

**Re-application path:**
- If upstream's RTClient.dll already uses compression for Synchronous packets:
  **DROP this entry**, the F1 motivation is gone.
- If upstream still has the false/false setup and we still want F1: this is
  not easily re-applicable. Compression bit is set inside their compiled DLL.
  Realistic re-application is a Harmony patch on `Serializer.ConvertObjectToBytes`
  / `ConvertBytesToObject` for Synchronous-path packets only — finicky to
  scope correctly.
- **Decision factor:** does upstream's RTClient.dll already do compression?
  If yes, this entry is obsolete. If no, F1 becomes much harder and may not
  be worth it.

---

## Change 4 — Instrumentation: VisitMetrics + call sites

**Intent:** Get **numbers** for the three suspected lag contributors
identified during pre-WIP baseline research (internal notes). Specifically:
- C1: session-start cost — `MapToString` ms + payload bytes on host,
  `StringToMap` + `ConvertBytesToObject` ms + bytes on guest
- C2: per-action lockstep round-trip — `PM_SJob.Ask` -> `Flush` -> `Handle`
  with pawnId-keyed timestamp dict
- C3: state-drift periodic snapshot — every 30s via `[OnUpdate]` log
  `side/spawnedPawns/allPawns/things/weather/tick` for diff between host
  and guest

The whole WIP exists primarily to enable this. Without C1/C2/C3 data we are
guessing.

**Surface:**
- NEW: `Source/Client/Misc/VisitMetrics.cs` — 120 lines, all `[VISIT-METRIC]`
  prefixed logs via `Printer.Warning`. `[OnSynchronousStart]` resets state.
  `[OnUpdate]` does periodic snapshot.
- `Source/Client/PacketManagers/Synchronous/PM_Synchronous.cs:117-122,203-212`
  — C1 wiring: `LogMapToString`, `LogAcceptContentsSize`,
  `LogReceiveAcceptContents`, `LogDeserializeFlMap`, `LogStringToMap`.
- `Source/Client/PacketManagers/Synchronous/PM_SJob.cs:45,72,92` — C2 wiring:
  `OnJobAsk`, `OnJobFlush`, `OnJobHandle`.

**Wire effect:** None. Pure logging.

**Re-eval after upgrade:**
- All instrumentation lives in `Source/Client/` which doesn't exist upstream.
  Cannot reach the relevant call sites without either source access or
  Harmony patches.
- **Reality check:** if upstream's binary improvements actually fix the lag
  (Nova's "trading code improvements" + "Ram improvements to maps and saves"
  may have indirectly improved the visit path), we may no longer NEED to
  measure. **Run a 3-player visit on the upgraded build first**, judge
  felt-quality. If it's noticeably better, drop instrumentation entirely.

**Re-application path (if we still need data after upgrade):**
- All three counters need Harmony patches in a new mod assembly. C1 attaches
  to `Serializer.ConvertObjectToBytes` / `ConvertBytesToObject` filtered to
  `PKT_Synchronous`. C2 attaches to whatever upstream renamed `PM_SJob.Ask`/
  `Handle` to. C3 is a fresh `IModWithUpdates` or Harmony patch on a known
  per-frame hook.
- Significant effort — ~1 session of Harmony exploration on upstream's
  decompiled RTClient.dll to find anchor points.
- **Only do this if the upgrade doesn't already fix the felt lag.**

---

## Change 5 — MessagePack NuGet bump (csproj only)

**Intent:** Mirror upstream's `bfbd6868 "Security patch for MessagePack"` —
which was actually just a NuGet version bump from `3.1.4` -> `3.1.7` (plus
`System.Security.Permissions 10.0.1` -> `10.0.9` on the server csproj).

**Surface:**
- `Source/Client/GameClient.csproj:37` — `MessagePack` 3.1.4 -> 3.1.7
- `Source/Server/GameServer.csproj:30` — `MessagePack` 3.1.4 -> 3.1.7
- `Source/Server/GameServer.csproj:33` — `System.Security.Permissions` 10.0.1 -> 10.0.9
- 3 lines total.

**Wire effect:** Potentially — MessagePack-CSharp is normally wire-stable
across patch versions but has had format-affecting changes historically.
**Not smoke-tested.**

**Re-eval after upgrade:**
- Drop this entry. The upgrade itself adopts upstream's csproj, which already
  has these bumps. Redundant after upgrade.

**Re-application path:** N/A (subsumed by the upgrade).

---

## Change 6 — Auto-updater pipeline (independent of upstream)

**Intent:** Friends run `UpdateRTMP.bat` before each play session;
the script enforces an exact-match copy of our latest `mp-*` GitHub release
over their Workshop install, defeating Steam Workshop auto-syncs.

**Surface (untracked, all NEW):**
- `Scripts/Updater/Update-RTMP.ps1` — main PowerShell updater (~345 lines)
- `Scripts/Updater/UpdateRTMP.bat` — double-click shim
- `Scripts/Updater/README.md` — friend-facing install + usage
- `Scripts/Release/Build-Release.ps1` — user-side build + package
- `docs/release-process.md` — user-side release runbook
- `docs/wip-catalog-pre-upgrade.md` — this file
- `.gitignore` — added `/dist/` and `!Scripts/Release/` un-ignore (defeats the
  Visual Studio `[Rr]elease/` build-output pattern)

**Wire effect:** None. Out-of-game infrastructure.

**Re-eval after upgrade:** Files SURVIVE the branch switch — none of these
paths exist in upstream's tree, no conflict on cherry-pick. However the
pipeline has TWO embedded assumptions that DON'T survive the upgrade and
need patching on the upgrade branch:
1. `Scripts/Release/Build-Release.ps1:73` only builds `1.6/Assemblies/`,
   but upstream supports both 1.5 and 1.6. Need to either drop `1.5` from
   `$ShipEntries` or add a 1.5 build step.
2. `docs/release-process.md:48` describes `RTNetwork.dll` + `RTShared.dll`
   as "copied from `Source/Assemblies/Shared/`" — upstream relocated those
   to `Source/Assemblies/` (no `Shared/` subfolder). Update the doc and any
   build script reference.
3. After the upgrade, `RTClient.dll` ships from upstream `Source/Assemblies/`
   prebuilt — no `dotnet build` step needed for the client. The build
   script's `Invoke-Build` function can be simplified to skip the client
   build entirely.

The pipeline becomes *more* useful after upgrade because the upgrade
requires a lockstep deployment to all three players — exactly what the
updater handles. Patch the three items above first; otherwise the first
post-upgrade release will fail at packaging time.

**Re-application path:** N/A. These files are not in upstream's diff
surface; the upgrade will not touch them.

---

## Cross-cutting: PKT_* type rename trap

This is not a single change but a hazard that affects re-application of
Changes 1, 3, and 4:

Upstream renamed PKT_* properties from underscore-prefix (`_stepMode`,
`_settlementTile`) to PascalCase (`StepMode`, `SettlementTile`) and
restructured `PKT_Map` to carry raw `byte[]` instead of `FL_Map`. These
property names are owned by `RTShared.dll`. After we upgrade to upstream's
`RTShared.dll`, any of our source we keep that references the old property
names will fail to compile.

**Implication:** the fork's `Source/Client/` tree is **architecturally
orphaned** after the upgrade. We cannot just "keep our client source on top
of upstream's RTShared.dll" — the API contracts don't match. We have two
viable patterns going forward:

1. **Drop fork's Source/Client/ entirely.** Use upstream's prebuilt
   `RTClient.dll`. Any client-side behavior we want gets re-implemented as
   Harmony patches in a new small mod assembly. This is the cleanest path.
2. **Port fork's Source/Client/ to upstream's API.** Big rewrite — every PKT
   reference needs updating to new property names, every `data.File` becomes
   `data.Bytes` + manual deserialization, etc. High effort, fragile.

Recommend pattern 1. Mostly because pattern 2 means we're maintaining a
private client fork against an opaque upstream — every upstream RTShared
change will break us.

---

## Re-evaluation checklist for after the upgrade

Walk in order:

1. **Change 5 (csproj bumps)** — DROP (subsumed by upgrade).
2. **Change 2 (ServerNetwork null-guard)** — re-read upstream's
   ServerNetwork.cs. Apply same 5-line diff if `OnDisconnect` still has
   unguarded `.Username` access. ~5 minutes.
3. **Change 6 (auto-updater)** — verify still works after upgrade. Tag a
   fresh `mp-*` release from the new base. Friends run UpdateRTMP.bat. Should
   "just work" since paths don't collide.
4. **Change 1 (MainThreadHandler try/catch)** — decompile upstream's
   RTClient.dll, check if try/catch is present. If absent, write the Harmony
   patch. If present, DROP.
5. **Run a 3-player visit on the upgraded build.** Judge felt-quality.
6. **If visit feels fixed:** DROP Changes 3 and 4. Done.
7. **If visit still feels bad:** decompile RTClient.dll, locate Synchronous
   PM_S* equivalents, write Harmony patches for Change 3 (compression flip)
   and/or Change 4 (instrumentation). This is the longest single re-app path.

## Decision log this catalog informs

- Whether to drop `Source/Client/` entirely vs port it (see "PKT_* type
  rename trap" — recommend drop).
- Whether to spend effort re-applying Changes 1/3/4 vs trusting upstream's
  binary fixes to have addressed the same problems (verdict by play-test).
- Whether the visit-feature work continues at all, or whether it becomes a
  patient project on a stable upstream base.
