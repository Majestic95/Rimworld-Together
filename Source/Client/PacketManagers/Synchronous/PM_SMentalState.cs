using GameClient.Hooks.Synchronous;
using GameClient.Misc;
using RTShared;
using System;
using RTNetwork;
using RTNetwork.PacketManagers;
using RTNetwork.Packets;
using Verse;
using Verse.AI;

namespace GameClient.PacketManagers.Synchronous
{
    public class PM_SMentalState : PM_Base
    {
        // We need a reference to the latest mental state so the game doesn't freak out while waiting for the server response

        public static MentalState LatestMentalState { get; set; } = null;

        public static void Ask(Pawn pawn, byte value, PlayerMentalState.MentalMode mode)
        {
            PlayerMentalState playerMentalState = new PlayerMentalState();
            playerMentalState.MapTile = pawn.Map.Tile;
            playerMentalState.PawnID = pawn.ThingID;
            playerMentalState.MentalStateByte = value;
            playerMentalState.Mode = mode;

            PKT_Synchronous packet = new PKT_Synchronous();
            packet.CurrentStepMode = PKT_Synchronous.StepMode.Action;
            packet.CurrentActionType = PKT_Synchronous.ActionType.SPlayerMentalState;
            packet.Contents = Serializer.ConvertObjectToBytes(playerMentalState, SynchronousOptions.CompressContents);

            Network.ServerEndpoint.EnqueuePacket(PacketHeader.Synchronous, packet);
        }

        public static void Handle(ServerClient client, PKT_Synchronous data)
        {
            PlayerMentalState mentalState = Serializer.ConvertBytesToObject<PlayerMentalState>(data.Contents, SynchronousOptions.CompressContents);

            PatchHandler.ExecuteInBypass(delegate
            {
                if (mentalState.Mode == PlayerMentalState.MentalMode.Add) AddMentalState(mentalState);
                else RemoveMentalState(mentalState);
            });
        }

        private static void AddMentalState(PlayerMentalState data)
        {
            Map map = Finder.GetMapFromTile(data.MapTile);
            Pawn pawn = Finder.GetPawnFromID(map, data.PawnID);
            MentalStateDef def = Finder.GetMentalStateDefFromByte(data.MentalStateByte);

            if (def != null) pawn.mindState.mentalStateHandler.TryStartMentalState(def);
        }

        private static void RemoveMentalState(PlayerMentalState data)
        {
            Map map = Finder.GetMapFromTile(data.MapTile);
            Pawn pawn = Finder.GetPawnFromID(map, data.PawnID);
            MentalStateDef def = Finder.GetMentalStateDefFromByte(data.MentalStateByte);

            if (def != null)
            {
                if (pawn.MentalState != null)
                {
                    pawn.MentalState.RecoverFromState();
                }
            }
        }

        public override void Receive(ServerClient client, byte[] bytes, PacketHeader header)
        {
            throw new NotImplementedException();
        }
    }
}
