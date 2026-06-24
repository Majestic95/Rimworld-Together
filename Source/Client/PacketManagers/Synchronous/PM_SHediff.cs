using GameClient.Hooks.Synchronous;
using GameClient.Misc;
using RTShared;
using System;
using System.Linq;
using RTNetwork;
using RTNetwork.PacketManagers;
using RTNetwork.Packets;
using Verse;

namespace GameClient.PacketManagers.Synchronous
{
    public class PM_SHediff : PM_Base
    {
        public static void Ask(Hediff hediff, BodyPartRecord bodyPart, Pawn pawn, PlayerHediff.HediffMode mode, float tendQuality = -1)
        {
            PlayerHediff playerHediff = new PlayerHediff();
            playerHediff.Mode = mode;
            playerHediff.MapTile = pawn.Map.Tile;
            playerHediff.PawnID = pawn.ThingID;
            playerHediff.HediffDefname = hediff.def.defName;
            playerHediff.PartDefname = bodyPart != null ? bodyPart.def.defName : null;
            playerHediff.Severity = hediff.Severity;
            playerHediff.IsPermanent = hediff.IsPermanent();
            playerHediff.TendQuality = tendQuality;

            PKT_Synchronous packet = new PKT_Synchronous();
            packet.CurrentStepMode = PKT_Synchronous.StepMode.Action;
            packet.CurrentActionType = PKT_Synchronous.ActionType.SPlayerHediff;
            packet.Contents = Serializer.ConvertObjectToBytes(playerHediff, SynchronousOptions.CompressContents);

            Network.ServerEndpoint.EnqueuePacket(PacketHeader.Synchronous, packet);
        }

        public static void Handle(ServerClient client, PKT_Synchronous data)
        {
            PlayerHediff playerHediff = Serializer.ConvertBytesToObject<PlayerHediff>(data.Contents, SynchronousOptions.CompressContents);

            PatchHandler.ExecuteInBypass(delegate
            {
                if (playerHediff.Mode == PlayerHediff.HediffMode.Add) AddHediff(playerHediff);
                else if (playerHediff.Mode == PlayerHediff.HediffMode.Remove) RemoveHediff(playerHediff);
                else UpdateHediff(playerHediff);
            });
        }

        private static void AddHediff(PlayerHediff data)
        {
            Map map = Finder.GetMapFromTile(data.MapTile);
            Pawn pawn = Finder.GetPawnFromID(map, data.PawnID);
            BodyPartRecord part = Finder.GetBodyPartFromDefname(pawn, data.PartDefname);

            HediffDef hediffDef = DefDatabase<HediffDef>.AllDefs.First(fetch => fetch.defName == data.HediffDefname);
            Hediff hediff = HediffMaker.MakeHediff(hediffDef, pawn, part);
            hediff.Severity = data.Severity;

            if (data.IsPermanent)
            {
                HediffComp_GetsPermanent hediffComp = hediff.TryGetComp<HediffComp_GetsPermanent>();
                hediffComp.IsPermanent = true;
            }

            if (hediff != null) pawn.health.AddHediff(hediff, part);
        }

        private static void RemoveHediff(PlayerHediff data)
        {
            Map map = Finder.GetMapFromTile(data.MapTile);
            Pawn pawn = Finder.GetPawnFromID(map, data.PawnID);
            BodyPartRecord part = Finder.GetBodyPartFromDefname(pawn, data.PartDefname);
            Hediff hediff = Finder.GetHediffFromPart(pawn, part, data.HediffDefname, false);

            if (hediff != null) pawn.health.RemoveHediff(hediff);
        }

        private static void UpdateHediff(PlayerHediff data)
        {
            Map map = Finder.GetMapFromTile(data.MapTile);
            Pawn pawn = Finder.GetPawnFromID(map, data.PawnID);
            BodyPartRecord part = Finder.GetBodyPartFromDefname(pawn, data.PartDefname);
            Hediff hediff = Finder.GetHediffFromPart(pawn, part, data.HediffDefname, true);

            if (hediff != null)
            {
                hediff.Severity = data.Severity;

                HediffComp_TendDuration comp = hediff.TryGetComp<HediffComp_TendDuration>();
                comp.CompTended(data.TendQuality, -1);
            }
        }

        public override void Receive(ServerClient client, byte[] bytes, PacketHeader header)
        {
            throw new NotImplementedException();
        }
    }
}
