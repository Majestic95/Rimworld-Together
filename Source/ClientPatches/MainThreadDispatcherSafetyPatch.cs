using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Majestic.RTMP.ClientPatches
{
    [StaticConstructorOnStartup]
    public static class ClientPatchesInit
    {
        private const string HarmonyId = "majestic95.rtmp.clientpatches";

        static ClientPatchesInit()
        {
            try
            {
                var harmony = new Harmony(HarmonyId);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Message("[ClientPatches] MainThread dispatcher guard applied.");
            }
            catch (Exception e)
            {
                // CRITICAL tag so this is grep-able in player.log. A failure here
                // usually means upstream renamed/moved MainThreadManager or
                // ExecuteAllQueue, and our crash insurance is now off. Without
                // a loud signal we would silently revert to upstream's buggy
                // behavior and the first sign would be a player drop.
                Log.Error("[ClientPatches][CRITICAL] init failed - crash insurance is OFF. Likely upstream rename. " + e);
            }
        }
    }

    // Replaces MainThreadManager.ExecuteAllQueue with a defensive copy that
    // wraps each queued callback in try/catch. Without this guard, a single
    // exception inside a queued packet handler propagates out of the dispatcher's
    // while-loop, eventually breaks the RT client's network state, and the
    // player is dropped with the standard "lost connection — save?" dialog
    // (see Joey crash 2026-06-23, Player.log line 337). The catch logs the
    // exception so the next repro names the actual originating call site.
    [HarmonyPatch]
    public static class Patch_MainThreadManager_ExecuteAllQueue
    {
        private static readonly Type MtmType =
            AccessTools.TypeByName("GameClient.Managers.MainThreadManager");

        private static readonly MethodInfo QueueGetter =
            MtmType == null ? null : AccessTools.PropertyGetter(MtmType, "ActionQueue");

        static MethodBase TargetMethod()
        {
            if (MtmType == null)
            {
                throw new Exception(
                    "ClientPatches: type GameClient.Managers.MainThreadManager not found in loaded assemblies. " +
                    "Upstream may have renamed or moved it.");
            }
            var method = AccessTools.Method(MtmType, "ExecuteAllQueue");
            if (method == null)
            {
                throw new Exception(
                    "ClientPatches: MainThreadManager.ExecuteAllQueue not found. " +
                    "Upstream may have renamed it.");
            }
            return method;
        }

        static bool Prefix()
        {
            // If reflection couldn't bind the queue, fall through to the original.
            // That's "as bad as today" (the original is the bug we're fixing),
            // not "worse than today".
            if (QueueGetter == null) return true;

            var queue = (Queue<Action>)QueueGetter.Invoke(null, null);
            if (queue == null) return false;

            lock (queue)
            {
                while (queue.Count > 0)
                {
                    Action queued = queue.Dequeue();
                    if (queued == null) continue;
                    try
                    {
                        queued.Invoke();
                    }
                    catch (Exception e)
                    {
                        Log.Error("[ClientPatches] MainThread dispatch failed: " + e);
                    }
                }
            }
            return false; // skip original
        }
    }
}
