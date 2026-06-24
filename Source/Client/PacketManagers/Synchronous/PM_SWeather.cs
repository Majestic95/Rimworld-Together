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
    public class PM_SWeather : PM_Base
    {
        public static void Ask(byte value)
        {
            PlayerWeather weather = new PlayerWeather();
            weather.MapTile = Find.CurrentMap.Tile;
            weather.WeatherByte = value;

            PKT_Synchronous packet = new PKT_Synchronous();
            packet.CurrentStepMode = PKT_Synchronous.StepMode.Action;
            packet.CurrentActionType = PKT_Synchronous.ActionType.SPlayerWeather;
            packet.Contents = Serializer.ConvertObjectToBytes(weather, SynchronousOptions.CompressContents);

            Network.ServerEndpoint.EnqueuePacket(PacketHeader.Synchronous, packet);
        }

        public static void Handle(ServerClient client, PKT_Synchronous data)
        {
            PlayerWeather weather = Serializer.ConvertBytesToObject<PlayerWeather>(data.Contents, SynchronousOptions.CompressContents);

            PatchHandler.ExecuteInBypass(delegate
            {
                WeatherDef def = Finder.GetWeatherDefFromByte(weather.WeatherByte);
                Finder.GetMapFromTile(weather.MapTile).weatherManager.TransitionTo(def);
            });
        }

        public override void Receive(ServerClient client, byte[] bytes, PacketHeader header)
        {
            throw new NotImplementedException();
        }
    }
}
