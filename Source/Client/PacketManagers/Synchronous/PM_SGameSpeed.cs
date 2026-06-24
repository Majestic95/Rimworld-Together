using GameClient.Hooks.Synchronous;
using GameClient.Misc;
using RTShared;
using System;
using RTNetwork;
using RTNetwork.PacketManagers;
using RTNetwork.Packets;
using Verse;

namespace GameClient.PacketManagers.Synchronous
{
    public class PM_SGameSpeed : PM_Base
    {
        public static TimeSpeed LatestGameSpeed { get; private set; } = TimeSpeed.Paused;

        [OnSynchronousStart]
        private static void SendFirstSpeed()
        {
            if (SessionHandler.IsSynchronousHost)
            {
                PlayerGameSpeed data = new PlayerGameSpeed();
                data.CurrentGameSpeed = (int)TimeSpeed.Paused;
                data.TimeTicks = Find.TickManager.TicksSinceSettle;

                PKT_Synchronous packet = new PKT_Synchronous();
                packet.CurrentStepMode = PKT_Synchronous.StepMode.Action;
                packet.CurrentActionType = PKT_Synchronous.ActionType.SPlayerGameSpeed;
                packet.Contents = Serializer.ConvertObjectToBytes(data, SynchronousOptions.CompressContents);

                Network.ServerEndpoint.EnqueuePacket(PacketHeader.Synchronous, packet);
            }
        }

        public static void Ask(TimeSpeed speed)
        {
            if (speed == LatestGameSpeed) return;
            else
            {
                PlayerGameSpeed data = new PlayerGameSpeed();
                data.CurrentGameSpeed = (int)speed;
                data.TimeTicks = Find.TickManager.TicksSinceSettle;

                PKT_Synchronous packet = new PKT_Synchronous();
                packet.CurrentStepMode = PKT_Synchronous.StepMode.Action;
                packet.CurrentActionType = PKT_Synchronous.ActionType.SPlayerGameSpeed;
                packet.Contents = Serializer.ConvertObjectToBytes(data, SynchronousOptions.CompressContents);

                Network.ServerEndpoint.EnqueuePacket(PacketHeader.Synchronous, packet);
            }
        }

        public static void Handle(ServerClient client, PKT_Synchronous data)
        {
            PlayerGameSpeed gameSpeed = Serializer.ConvertBytesToObject<PlayerGameSpeed>(data.Contents, SynchronousOptions.CompressContents);

            PatchHandler.ExecuteInBypass(delegate
            {
                Find.TickManager.CurTimeSpeed = (TimeSpeed)gameSpeed.CurrentGameSpeed;
                Find.TickManager.DebugSetTicksGame(gameSpeed.TimeTicks);
                LatestGameSpeed = Find.TickManager.CurTimeSpeed;
            });
        }

        public override void Receive(ServerClient client, byte[] bytes, PacketHeader header)
        {
            throw new NotImplementedException();
        }
    }
}
