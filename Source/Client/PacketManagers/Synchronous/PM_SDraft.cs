using GameClient.Hooks.Synchronous;
using GameClient.Misc;
using RimWorld;
using RTShared;
using System;
using System.Collections.Generic;
using RTNetwork;
using RTNetwork.PacketManagers;
using RTNetwork.Packets;
using Verse;

namespace GameClient.PacketManagers.Synchronous
{
    public class PM_SDraft : PM_Base
    {
        private static List<PlayerDraft> PlayerDrafts { get; set; } = new List<PlayerDraft>();

        [OnSessionStart]
        private static void Initialize() { PlayerDrafts = new List<PlayerDraft>(); }

        [OnUpdate]
        private static void Check()
        {
            if (PlayerDrafts.Count > 0)
            {
                PKT_Synchronous packet = new PKT_Synchronous();
                packet.CurrentStepMode = PKT_Synchronous.StepMode.Action;
                packet.CurrentActionType = PKT_Synchronous.ActionType.SPlayerDraft;
                packet.Contents = Serializer.ConvertObjectToBytes(PlayerDrafts, SynchronousOptions.CompressContents);

                Network.ServerEndpoint.EnqueuePacket(PacketHeader.Synchronous, packet);

                PlayerDrafts.Clear();
            }
        }

        public static void Ask(Pawn pawn, bool mode) 
        {
            PlayerDraft draft = new PlayerDraft();
            draft.MapTile = pawn.Map.Tile;
            draft.PawnID = pawn.ThingID;
            draft.DraftValue = mode;

            PlayerDrafts.Add(draft); 
        }

        public static void Handle(ServerClient client, PKT_Synchronous data)
        {
            PlayerDraft[] drafts = Serializer.ConvertBytesToObject<PlayerDraft[]>(data.Contents, SynchronousOptions.CompressContents);

            PatchHandler.ExecuteInBypass(delegate
            {
                foreach (PlayerDraft playerDraft in drafts)
                {
                    Map map = Finder.GetMapFromTile(playerDraft.MapTile);
                    Pawn pawn = Finder.GetPawnFromID(map, playerDraft.PawnID);

                    pawn.drafter ??= new Pawn_DraftController(pawn);
                    pawn.drafter.Drafted = playerDraft.DraftValue;
                }
            });
        }

        public override void Receive(ServerClient client, byte[] bytes, PacketHeader header)
        {
            throw new NotImplementedException();
        }
    }
}
