using RTShared;
using RTShared.Misc;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Verse;

namespace GameClient.Misc
{
    /// <summary>
    /// Temporary instrumentation for the synchronous-sessions baseline test.
    /// All log lines are prefixed [VISIT-METRIC] for easy grep + later removal.
    ///
    /// Three measurement categories:
    ///   C1 — session-start cost (map serialize/deserialize wall-clock + payload size)
    ///   C2 — per-action lockstep round-trip (PM_SJob Ask -> flush -> Handle)
    ///   C3 — state-drift snapshots (periodic during a live visit)
    ///
    /// Delete this file and the corresponding call sites once the baseline
    /// is captured. Grep [VISIT-METRIC] / VisitMetrics\. to find every site.
    /// </summary>
    public static class VisitMetrics
    {
        private const string Tag = "[VISIT-METRIC]";
        private const int MaxAskMapEntries = 256;
        private const float SnapshotIntervalSeconds = 30f;

        private static readonly Dictionary<string, long> jobAskTimestamps = new Dictionary<string, long>();
        private static float lastSnapshotUnscaledTime = float.MinValue;

        // ---------- C1: session-start ----------

        public static void LogMapToString(long elapsedMs)
        {
            Printer.Warning($"{Tag} C1.MapToString elapsedMs={elapsedMs}");
        }

        public static void LogAcceptContentsSize(int bytes, bool compressionFlag)
        {
            Printer.Warning($"{Tag} C1.AcceptContents bytes={bytes} compressionFlag={compressionFlag}");
        }

        public static void LogReceiveAcceptContents(int bytes, bool compressionFlag)
        {
            Printer.Warning($"{Tag} C1.ReceiveAccept bytes={bytes} compressionFlag={compressionFlag}");
        }

        public static void LogDeserializeFlMap(long elapsedMs)
        {
            Printer.Warning($"{Tag} C1.DeserializeFlMap elapsedMs={elapsedMs}");
        }

        public static void LogStringToMap(long elapsedMs)
        {
            Printer.Warning($"{Tag} C1.StringToMap elapsedMs={elapsedMs}");
        }

        // ---------- C2: job round-trip ----------

        public static void OnJobAsk(string pawnId)
        {
            if (jobAskTimestamps.Count >= MaxAskMapEntries) jobAskTimestamps.Clear();
            jobAskTimestamps[pawnId] = Stopwatch.GetTimestamp();
            Printer.Warning($"{Tag} C2.JobAsk pawnId={pawnId}");
        }

        public static void OnJobFlush(int count, int payloadBytes)
        {
            Printer.Warning($"{Tag} C2.JobFlush count={count} payloadBytes={payloadBytes}");
        }

        public static void OnJobHandle(string pawnId)
        {
            if (jobAskTimestamps.TryGetValue(pawnId, out long askTs))
            {
                long elapsedMs = (Stopwatch.GetTimestamp() - askTs) * 1000L / Stopwatch.Frequency;
                Printer.Warning($"{Tag} C2.JobHandle pawnId={pawnId} roundtripMs={elapsedMs}");
                jobAskTimestamps.Remove(pawnId);
            }
            else
            {
                Printer.Warning($"{Tag} C2.JobHandle pawnId={pawnId} roundtripMs=UNKNOWN (no Ask)");
            }
        }

        // ---------- C3: periodic state snapshot ----------

        [OnSynchronousStart]
        private static void ResetSessionState()
        {
            jobAskTimestamps.Clear();
            lastSnapshotUnscaledTime = float.MinValue;
            Printer.Warning($"{Tag} session_start IsHost={SessionHandler.IsSynchronousHost}");
        }

        [OnUpdate]
        private static void PeriodicStateSnapshot()
        {
            Map map = SessionHandler.SynchronousMap;
            if (map == null) return;
            if (Time.unscaledTime - lastSnapshotUnscaledTime < SnapshotIntervalSeconds) return;
            lastSnapshotUnscaledTime = Time.unscaledTime;

            int pawnCount = 0;
            int allPawnsTotal = 0;
            int thingCount = 0;
            int tick = 0;
            string weather = "none";
            string side = SessionHandler.IsSynchronousHost ? "HOST" : "GUEST";

            try { allPawnsTotal = map.mapPawns.AllPawns.Count; } catch { allPawnsTotal = -1; }
            try { pawnCount = map.mapPawns.AllPawnsSpawned.Count; } catch { pawnCount = -1; }
            try { thingCount = map.listerThings.AllThings.Count; } catch { thingCount = -1; }
            try { weather = map.weatherManager?.curWeather?.defName ?? "none"; } catch { weather = "ERR"; }
            try { tick = Find.TickManager.TicksGame; } catch { tick = -1; }

            Printer.Warning($"{Tag} C3.Snapshot side={side} spawnedPawns={pawnCount} allPawns={allPawnsTotal} things={thingCount} weather={weather} tick={tick}");
        }
    }
}
