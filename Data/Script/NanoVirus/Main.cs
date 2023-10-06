using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;

namespace Kage.HackingComputer
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class Main : MySessionComponentBase
    {
        private bool m_init = false;

        public override void UpdateBeforeSimulation()
        {
            if (!m_init && MyAPIGateway.Session != null)
            {
                //VRage.Utils.MyLog.Default.WriteLineAndConsole("Attempting to initialize HackingComputerSync");
                if (HackingComputerSync.Initialize()) m_init = true;
            }
        }

        protected override void UnloadData()
        {
            HackingComputerSync.Unload();
            LogManager.Unload();
        }
    }
    
    public static class Sync
    {
        public static bool IsServer
        {
            get
            {
                if (MyAPIGateway.Session == null)
                    return false;

                if (MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE || MyAPIGateway.Multiplayer.IsServer)
                    return true;

                return false;
            }
        }

        public static bool IsClient
        {
            get
            {
                if (MyAPIGateway.Session == null)
                    return false;

                if (MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE)
                    return true;

                if (MyAPIGateway.Session.Player != null && MyAPIGateway.Session.Player.Client != null && MyAPIGateway.Multiplayer.IsServerPlayer(MyAPIGateway.Session.Player.Client))
                    return true;

                if (!MyAPIGateway.Multiplayer.IsServer)
                    return true;

                return false;
            }
        }
    }

    public static class Tools
    {
        public static void UpdateTerminalClient(MyCubeBlock block)
        {
            MyOwnershipShareModeEnum shareMode;
            long ownerId;

            if (block.IDModule != null)
            {
                ownerId = block.IDModule.Owner;
                shareMode = block.IDModule.ShareMode;
            }
            else return;

            block.ChangeOwner(ownerId, shareMode == MyOwnershipShareModeEnum.None ? MyOwnershipShareModeEnum.Faction : MyOwnershipShareModeEnum.None);
            block.ChangeOwner(ownerId, shareMode);
        }
    }
}