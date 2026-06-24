using GameClient.Dialogs;
using GameClient.Dialogs.Default;
using GameClient.Managers;
using GameClient.Misc;
using RimWorld;
using RimWorld.Planet;
using RTShared;
using RTShared.Files;
using RTShared.Files.Synchronous;
using RTShared.Misc;
using System;
using System.Linq;
using RTNetwork;
using RTNetwork.PacketManagers;
using RTNetwork.Packets;
using Verse;
using Verse.Noise;

namespace GameClient.PacketManagers.Synchronous
{
    public class PM_Synchronous : PM_Base
    {
        private enum SynchronousSide { Host, Guest }

        [HandlesPacket(PacketHeader.Synchronous)]
        public override void Receive(ServerClient client, byte[] bytes, PacketHeader header)
        {
            PKT_Synchronous data = Serializer.ConvertBytesToObject<PKT_Synchronous>(bytes);

            switch (data.CurrentStepMode)
            {
                case PKT_Synchronous.StepMode.Ask:
                    OnAsk(data);
                    break;

                case PKT_Synchronous.StepMode.Accept:
                    OnAccept(data);
                    break;

                case PKT_Synchronous.StepMode.Reject:
                    OnReject(data);
                    break;

                case PKT_Synchronous.StepMode.Start:
                    StartSession(SynchronousSide.Host);
                    break;

                case PKT_Synchronous.StepMode.Action:
                    RouteToManager(client, data, data.CurrentActionType);
                    break;
            }
        }

        private static void RouteToManager(ServerClient client, PKT_Synchronous data, PKT_Synchronous.ActionType currentAction)
        {
            switch (currentAction)
            {
                case PKT_Synchronous.ActionType.SPlayerDraft:
                    PM_SDraft.Handle(client, data);
                    break;

                case PKT_Synchronous.ActionType.SPlayerWeather:
                    PM_SWeather.Handle(client, data);
                    break;

                case PKT_Synchronous.ActionType.SPlayerMentalState:
                    PM_SMentalState.Handle(client, data);
                    break;

                case PKT_Synchronous.ActionType.SPlayerGameSpeed:
                    PM_SGameSpeed.Handle(client, data);
                    break;

                case PKT_Synchronous.ActionType.SPlayerJob:
                    PM_SJob.Handle(client, data);
                    break;

                case PKT_Synchronous.ActionType.SPlayerHediff:
                    PM_SHediff.Handle(client, data);
                    break;

                case PKT_Synchronous.ActionType.SPlayerDestroy:
                    PM_SDestroy.Handle(client, data);
                    break;
            }
        }

        public static void Ask(int tile, PKT_Synchronous.Type type)
        {
            DLG_Base.PushNewDialog(new DLG_Wait());

            PKT_Synchronous data = new PKT_Synchronous();
            data.CurrentStepMode = PKT_Synchronous.StepMode.Ask;
            data.ToTile = tile;
            data.CurrentType = type;
            data.Party = GetPawnParty(SynchronousSide.Guest);

            Network.ServerEndpoint.EnqueuePacket(PacketHeader.Synchronous, data);
        }

        private static void OnAsk(PKT_Synchronous data)
        {
            DLG_Wait.Instance.Close();

            Action actionYes = delegate
            {
                DLG_Base.PushNewDialog(new DLG_Wait());

                SetMap(SynchronousSide.Host, null);

                PKT_Synchronous _ = new PKT_Synchronous();
                _.CurrentStepMode = PKT_Synchronous.StepMode.Accept;
                _.CurrentType = data.CurrentType;
                _.FromTile = data.ToTile;
                _.ToTile = data.FromTile;
                _.Party = GetPawnParty(SynchronousSide.Host);

                var __mapSw = System.Diagnostics.Stopwatch.StartNew();
                var __flMap = MapSaveLoader.MapToString(SessionHandler.SynchronousMap);
                __mapSw.Stop();
                VisitMetrics.LogMapToString(__mapSw.ElapsedMilliseconds);
                _.Contents = Serializer.ConvertObjectToBytes(__flMap, SynchronousOptions.CompressContents);
                VisitMetrics.LogAcceptContentsSize(_.Contents.Length, SynchronousOptions.CompressContents);

                SpawnOtherPawns(SynchronousSide.Host, data);

                Network.ServerEndpoint.EnqueuePacket(PacketHeader.Synchronous, _);
            };

            Action actionNo = delegate
            {
                PKT_Synchronous _ = new PKT_Synchronous();
                _.CurrentStepMode = PKT_Synchronous.StepMode.Reject;
                _.FromTile = data.ToTile;
                _.ToTile = data.FromTile;

                Network.ServerEndpoint.EnqueuePacket(PacketHeader.Synchronous, _);
            };

            if (!DLG_Options.EnablePreviewFeatures) actionNo();
            else
            {
                string description = $"Player '{data.Username}' wants to interact, accept?";
                DLG_Base.PushNewDialog(new DLG_YesNo(description, actionYes, actionNo));
            }
        }

        private static void OnAccept(PKT_Synchronous data)
        {
            DLG_Wait.Instance.Close();

            SetMap(SynchronousSide.Guest, data);

            EnterMap(SynchronousSide.Guest);

            StartSession(SynchronousSide.Guest);
        }

        private static void OnReject(PKT_Synchronous data)
        {
            DLG_Wait.Instance.Close();

            string[] description = new string[] { "Interaction was rejected by the player!" };
            DLG_Base.PushNewDialog(new DLG_Message(null, description));
        }

        private static void StartSession(SynchronousSide side)
        {
            if (side == SynchronousSide.Host)
            {
                SessionHandler.IsSynchronousHost = true;
                MainThreadHandler.Instance.DoOnSynchronousStartMethods();
                DLG_Wait.Instance.Close();

                string[] description = new string[] { "Game will be unable to save while in synchronous!" };
                DLG_Base.PushNewDialog(new DLG_Message("MESSAGE", description));
            }

            else
            {
                PKT_Synchronous data = new PKT_Synchronous();
                data.CurrentStepMode = PKT_Synchronous.StepMode.Start;
                Network.ServerEndpoint.EnqueuePacket(PacketHeader.Synchronous, data);

                MainThreadHandler.Instance.DoOnSynchronousStartMethods();

                string[] description = new string[] { "Game will be unable to save while in synchronous!" };
                DLG_Base.PushNewDialog(new DLG_Message("MESSAGE", description));
            }
        }

        private static void EndSession()
        {
            MainThreadHandler.Instance.DoOnSynchronousEndMethods();
            SessionHandler.IsSynchronousHost = false;
        }

        private static void SetMap(SynchronousSide side, PKT_Synchronous data)
        {
            if (side == SynchronousSide.Host) SessionHandler.SynchronousMap = Find.AnyPlayerHomeMap;
            else
            {
                VisitMetrics.LogReceiveAcceptContents(data.Contents.Length, SynchronousOptions.CompressContents);
                var __dSw = System.Diagnostics.Stopwatch.StartNew();
                FL_Map __flMap = Serializer.ConvertBytesToObject<FL_Map>(data.Contents, SynchronousOptions.CompressContents);
                __dSw.Stop();
                VisitMetrics.LogDeserializeFlMap(__dSw.ElapsedMilliseconds);

                var __sSw = System.Diagnostics.Stopwatch.StartNew();
                SessionHandler.SynchronousMap = MapSaveLoader.StringToMap(__flMap, true);
                __sSw.Stop();
                VisitMetrics.LogStringToMap(__sSw.ElapsedMilliseconds);

                foreach (Pawn pawn in SessionHandler.SynchronousMap.mapPawns.AllPawns.Where(fetch => fetch.Faction == Faction.OfPlayer))
                {
                    if (data.CurrentType == PKT_Synchronous.Type.Visit) pawn.SetFactionDirect(SessionHandler.AllyFaction);
                    else pawn.SetFactionDirect(SessionHandler.EnemyFaction);
                }
            }
        }

        private static void EnterMap(SynchronousSide side)
        {
            if (side == SynchronousSide.Host)
            {
                CameraJumper.TryJump(SessionHandler.SynchronousMap.Center, SessionHandler.SynchronousMap, CameraJumper.MovementMode.Pan);
            }

            else
            {
                CaravanEnterMapUtility.Enter(SessionHandler.ChosenCaravan, SessionHandler.SynchronousMap, CaravanEnterMode.Center,
                    CaravanDropInventoryMode.DoNotDrop, draftColonists: false);

                CameraJumper.TryJump(SessionHandler.SynchronousMap.Center, SessionHandler.SynchronousMap, CameraJumper.MovementMode.Pan);
            }
        }

        private static void SpawnOtherPawns(SynchronousSide side, PKT_Synchronous data)
        {
            if (side == SynchronousSide.Host)
            {
                foreach (string str in data.Party.Pawns)
                {
                    Pawn pawn = ScribeManager.SerializeFromString<Pawn>(str, ScribeManager.SerializableType.Pawn, true);

                    RimworldManager.PlaceThingIntoMap(pawn, SessionHandler.SynchronousMap,
                        side == SynchronousSide.Host ? SessionHandler.SynchronousMap.Center : pawn.PositionHeld);

                    if (data.CurrentType == PKT_Synchronous.Type.Visit) pawn.SetFactionDirect(SessionHandler.AllyFaction);
                    else pawn.SetFactionDirect(SessionHandler.EnemyFaction);
                }
            }
        }

        private static SyncronousParty GetPawnParty(SynchronousSide side)
        {
            SyncronousParty file = new SyncronousParty();

            if (side == SynchronousSide.Host) file.Pawns = RimworldManager.GetMapPawnsIntoString(SessionHandler.SynchronousMap, true, true);
            else file.Pawns = RimworldManager.GetCaravanPawnsIntoString(SessionHandler.ChosenCaravan, true);

            return file;
        }
    }
}
