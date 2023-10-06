using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using System.Text;
using VRage.Game.ModAPI;
using VRage.Game;
using VRage.Game.ObjectBuilders.Definitions;

namespace Kage.HackingComputer
{
	[MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), true, "LargeFirewallBlock", "SmallFirewallBlock")]
    public class FirewallBlock : MyGameLogicComponent
    {
        private IMyFunctionalBlock m_functionalBlock = null;
        private MyDefinitionId m_electricityDefinition = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");
        private MyResourceSinkComponent m_resourceSink;
        private bool m_powerAvailable;
        private float m_powerConsumption = 0.01f;
        private MyCubeBlock m_cubeBlock;

        private MyEntity3DSoundEmitter m_soundEmitter = null;
        private MySoundPair m_soundPair = MySoundPair.Empty;

        public const string Emissive1 = "EM_Firewall";
        public const string Emissive2 = "EM_Firewall2";

        public int BlockedAttempts;
        private int blockIndicatorCountdown, m_timer;
        private bool m_color, m_timerSet;
        private States m_state;
        private States m_lastState;

        private readonly int debug = 0;

        private enum States
        {
            Off,
            Defending,
            Idle
        };

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            MyLog.Default.WriteLineAndConsole("Initializing Firewallblock");

            if (Sync.IsClient)
                Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
            
            Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;

            m_functionalBlock = Entity as IMyFunctionalBlock;
            m_cubeBlock = m_functionalBlock as MyCubeBlock;

            InitPowerSystem();

            m_functionalBlock.AppendingCustomInfo += UpdateInfo;

            m_functionalBlock.SetEmissiveParts(Emissive1, Color.Red, 1f);
            m_functionalBlock.SetEmissiveParts(Emissive2, Color.Red, 1f);
        }

        private void InitPowerSystem()
        {
            var powerSystem = new MyResourceSinkComponent();
            var sinkInfo = new MyResourceSinkInfo();
            sinkInfo.ResourceTypeId = m_electricityDefinition;
            sinkInfo.MaxRequiredInput = m_powerConsumption;
            sinkInfo.RequiredInputFunc = new Func<float>(RequiredInputFunc);
            powerSystem.AddType(ref sinkInfo);
            Entity.Components.Add<MyResourceSinkComponent>(powerSystem);
            m_resourceSink = Entity.Components.Get<MyResourceSinkComponent>();
        }

        private float RequiredInputFunc()
        {
            if (!m_functionalBlock.Enabled || !m_cubeBlock.IsFunctional)
                return 0f;

            var powerToUse = m_powerConsumption;

            if (m_state == States.Idle)
                powerToUse = 0.0001f;

            //if (m_resourceSink.IsPowerAvailable(m_electricityDefinition, powerToUse))
            if (m_resourceSink.SuppliedRatioByType(m_electricityDefinition) == 1)
            {
                m_powerAvailable = true;
                return powerToUse;
            }
            else
            {
                m_powerAvailable = false;
                return 0f;
            }
        }

        private void UpdateInfo(IMyTerminalBlock arg1, StringBuilder arg2)
        {
            arg2.Append("- Anti-Hack Firewall v3.0 -\n\n");

            if (BlockedAttempts > 0)
                arg2.Append("WARNING! FIREWALL IS UNDER CYBERATTACK!!");
            else
                arg2.Append("No threat detected...");
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return null;
        }

        public override void UpdateBeforeSimulation100()
        {
            m_resourceSink.Update();

            if (Sync.IsClient && MyAPIGateway.Gui?.GetCurrentScreen == MyTerminalPageEnum.ControlPanel)
            { // Force terminal screen to refresh if being viewed
                m_functionalBlock.RefreshCustomInfo();
                Tools.UpdateTerminalClient((MyCubeBlock)m_functionalBlock);
                m_functionalBlock.ShowInToolbarConfig = false;
                m_functionalBlock.ShowInToolbarConfig = true;
            }

            if (BlockedAttempts > 0)
            {
                if (!m_timerSet)
                {
                    m_timerSet = true;
                    m_timer = 3;
                }
            }  
            else m_timerSet = false;

            if (m_timerSet && --m_timer < 1)
                BlockedAttempts = 0;
        }

        public bool IsWorking()
        {
            if (m_powerAvailable && m_cubeBlock.IsFunctional && m_functionalBlock.Enabled)
                return true;

            return false;
        }

        public override void UpdateBeforeSimulation10()
        {
            if (m_state != m_lastState)
            {
                if (m_state == States.Off)
                {
                    m_functionalBlock.SetEmissiveParts(Emissive1, Color.Red, 1f);
                    m_functionalBlock.SetEmissiveParts(Emissive2, Color.Red, 1f);
                }
                else if (m_state == States.Defending)
                {
                    m_functionalBlock.SetEmissiveParts(Emissive2, Color.OrangeRed, 1f);
                    m_functionalBlock.SetEmissiveParts(Emissive1, Color.Green, 1f);
                }
                else
                {
                    m_functionalBlock.SetEmissiveParts(Emissive2, Color.Cyan, 1f);
                    m_functionalBlock.SetEmissiveParts(Emissive1, Color.Green, 1f);
                }

                m_lastState = m_state;
            }

            if (!m_powerAvailable)
            {
                m_state = States.Off;
                return;
            }

            if (BlockedAttempts > 0)
                m_color = !m_color;
            else
                m_color = false;

            if (m_color)
                m_state = States.Defending;
            else
                m_state = States.Idle;            
        }
    }
}