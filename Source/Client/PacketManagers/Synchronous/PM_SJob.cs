using GameClient.Hooks.Synchronous;
using GameClient.Managers;
using GameClient.Misc;
using RimWorld;
using RTShared;
using Synchronous.Misc;
using System;
using System.Collections.Generic;
using System.Linq;
using RTNetwork;
using RTNetwork.PacketManagers;
using RTNetwork.Packets;
using UnityEngine;
using Verse;
using Verse.AI;

namespace GameClient.PacketManagers.Synchronous
{
    public class PM_SJob : PM_Base
    {
        private static List<PlayerJob> PlayerJobs { get; set; } = new List<PlayerJob>();

        private static double UpdateTimer { get; set; } = -1;

        [OnSessionStart]
        private static void Initialize() 
        { 
            PlayerJobs = new List<PlayerJob>();
            UpdateTimer = 0;
        }

        [OnUpdate]
        private static void Check()
        {
            if (UpdateTimer < 1f) UpdateTimer += Time.deltaTime;
            else
            {
                if (PlayerJobs.Count > 0)
                {
                    PKT_Synchronous packet = new PKT_Synchronous();
                    packet.CurrentStepMode = PKT_Synchronous.StepMode.Action;
                    packet.CurrentActionType = PKT_Synchronous.ActionType.SPlayerJob;
                    packet.Contents = Serializer.ConvertObjectToBytes(PlayerJobs, SynchronousOptions.CompressContents);

                    VisitMetrics.OnJobFlush(PlayerJobs.Count, packet.Contents.Length);
                    Network.ServerEndpoint.EnqueuePacket(PacketHeader.Synchronous, packet);

                    PlayerJobs.Clear();
                }

                UpdateTimer = 0;
            }
        }

        public static void Ask(Job job, Pawn pawn)
        {
            if (PlayerJobs.FirstOrDefault(fetch => fetch.PawnID == pawn.ThingID) != null) return;

            try
            {
                PlayerJob newJob = new PlayerJob();
                newJob.PawnID = pawn.ThingID;
                newJob.PawnPosition = TypeConverter.IntVector3ToString(pawn.Position);
                newJob.TargetA = TypeConverter.LocalTargetInfoToString(job.GetTarget(TargetIndex.A));
                newJob.TargetB = TypeConverter.LocalTargetInfoToString(job.GetTarget(TargetIndex.B));
                newJob.TargetC = TypeConverter.LocalTargetInfoToString(job.GetTarget(TargetIndex.C));
                newJob.QueueA = TypeConverter.LocalTargetQueueToString(job.GetTargetQueue(TargetIndex.A));
                newJob.QueueB = TypeConverter.LocalTargetQueueToString(job.GetTargetQueue(TargetIndex.B));
                newJob.Job = ScribeManager.SerializeToString(job, ScribeManager.SerializableType.Other);

                PlayerJobs.Add(newJob);
                VisitMetrics.OnJobAsk(pawn.ThingID);

                PatchHandler.ExecuteInBypass(delegate
                {
                    pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 1000),
                    JobCondition.InterruptForced);
                });
            }
            catch { }
        }
        public static void Handle(ServerClient client, PKT_Synchronous data)
        {
            PlayerJob[] jobs = Serializer.ConvertBytesToObject<PlayerJob[]>(data.Contents, SynchronousOptions.CompressContents);

            PatchHandler.ExecuteInBypass(delegate
            {
                foreach (PlayerJob playerJob in jobs)
                {
                    try
                    {
                        VisitMetrics.OnJobHandle(playerJob.PawnID);
                        Job newJob = ScribeManager.SerializeFromString<Job>(playerJob.Job);
                        newJob = TypeConverter.PlayerJobToJob(newJob, playerJob);

                        Pawn pawn = Finder.GetPawnFromID(SessionHandler.SynchronousMap, playerJob.PawnID);
                        pawn.SetPositionDirect(TypeConverter.StringToIntVec3(playerJob.PawnPosition));
                        pawn.jobs.StartJob(newJob, JobCondition.InterruptForced);
                    }
                    catch { };
                }
            });
        }

        public override void Receive(ServerClient client, byte[] bytes, PacketHeader header)
        {
            throw new NotImplementedException();
        }
    }
}
