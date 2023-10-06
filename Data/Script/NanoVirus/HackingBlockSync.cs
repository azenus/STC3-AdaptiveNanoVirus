using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Kage.HackingComputer
{
    public class HackingComputerSync
    {
        public static bool HasBeenInitialized { get; private set; } = false;

        public static ushort HackingStateMessageId = 23889;

        public static bool Initialize()
        {
            if (MyAPIGateway.Session.Player != null && HasBeenInitialized == false)
            {
                //VRage.Utils.MyLog.Default.WriteLineAndConsole("Initializing HackingComputerSync");
                MyAPIGateway.Multiplayer.RegisterMessageHandler(HackingStateMessageId, HandleHackingBlockStates);
                HasBeenInitialized = true;
                return true;
            }
            return false;
        }

        public static void Unload()
        {
            HasBeenInitialized = false;
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(HackingStateMessageId, HandleHackingBlockStates);
        }

        private static void HandleHackingBlockStates(byte[] m)
        {
            try
            {
                long entityId = BitConverter.ToInt64(m, 0);
                IMyEntity entity;
                if (MyAPIGateway.Entities.TryGetEntityById(entityId, out entity))
                {
                    HackingBlock.States state = (HackingBlock.States)BitConverter.ToInt32(m, 8);
                    long targetId = BitConverter.ToInt64(m, 12);
                    int chance = BitConverter.ToInt32(m, 20);
                    int attempts = BitConverter.ToInt32(m, 24);

                    var logic = entity.GameLogic.GetAs<HackingBlock>();
                    if (logic != null)
                    {
                        logic.CurrentState = state;
                        logic.TargetId = targetId;
                        logic.Chance = chance;
                        logic.Attempts = attempts;
                        logic.UpdateClient();

                        IMyEntity target = null;
                        MyAPIGateway.Entities.TryGetEntityById(targetId, out target);
                        if (target != null)
                        {
                            FirewallBlock firewall = target.GameLogic.GetAs<FirewallBlock>();
                            if (firewall != null)
                            {
                                if (state == HackingBlock.States.Hacking)
                                    firewall.BlockedAttempts++;
                                else if (state == HackingBlock.States.Success)
                                    firewall.BlockedAttempts = 0;
                            }
                        }
                    }
                    else
                        VRage.Utils.MyLog.Default.WriteLineAndConsole("handleHackingBlockStates: Unable to get GameLogic Component");
                }
            }
            catch (Exception e)
            {
                VRage.Utils.MyLog.Default.WriteLineAndConsole(e.ToString());
            }
        }

        public static void SendHackingBlockStates(HackingBlock b)
        {
            if (b == null || b.Entity == null)
                return;

            MyAPIGateway.Parallel.Start(() =>
            {
                try
                {
                    List<IMyPlayer> players = new List<IMyPlayer>();
                    MyAPIGateway.Multiplayer.Players.GetPlayers(players);

                    var distSq = MyAPIGateway.Session.SessionSettings.SyncDistance;
                    distSq *= distSq;

                    var syncPosition = b.Entity.GetPosition();

                    byte[] message = new byte[28];
                    Array.Copy(BitConverter.GetBytes(b.Entity.EntityId), 0, message, 0, 8);
                    Array.Copy(BitConverter.GetBytes((int)b.CurrentState), 0, message, 8, 4);
                    Array.Copy(BitConverter.GetBytes(b.TargetId), 0, message, 12, 8);
                    Array.Copy(BitConverter.GetBytes(b.Chance), 0, message, 20, 4);
                    Array.Copy(BitConverter.GetBytes(b.Attempts), 0, message, 24, 4);

                    foreach (var p in players)
                        if (p != null && Vector3D.DistanceSquared(p.GetPosition(), syncPosition) <= distSq)
                            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                            { 
                                if (p != null)
                                    MyAPIGateway.Multiplayer.SendMessageTo(HackingStateMessageId, message, p.SteamUserId); 
                            });
                }
                catch (Exception e)
                    { VRage.Utils.MyLog.Default.WriteLineAndConsole($"HackingBlockSync.SendHackingBlockStates exception: {e}"); }
            });
        }
    }
}