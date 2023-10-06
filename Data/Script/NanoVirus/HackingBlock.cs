using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using IMyLandingGear = SpaceEngineers.Game.ModAPI.IMyLandingGear;

namespace Kage.HackingComputer
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_UpgradeModule), true, "LargeHackingBlock", "SmallHackingBlock")]
    public class HackingBlock : MyGameLogicComponent
    {
        private IMyFunctionalBlock m_functionalBlock;
        private MyCubeBlock m_cubeBlock;
        MyResourceSinkComponent m_resourceSink;
        MyDefinitionId m_electricityDefinition = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");
        static Random m_random = new Random();
        private bool m_powerAvailable;
        MyObjectBuilder_EntityBase objectBuilder;

        public enum States
        {
            Off,
            NoEnemies,
            Hacking,
            Success,
            NewTarget
        };

        public States CurrentState = States.Off;
        public long TargetId;
        public int Chance;
        public int Attempts;
        private int m_updateTimer;
        private int m_lastupdate;
        private int m_lastSuccess;
        private long m_gridEntityId;
        private float m_powerConsumption = 0.01f;
        long m_ownerId;

        bool m_forceGridGroupUpdate;
        bool m_quickUpdate;

        private MyEntity3DSoundEmitter m_soundEmitter = null;
        private MySoundPair m_soundPair = MySoundPair.Empty;

        public const string Emissive = "Em_Hacking";
        private int m_countdown;

        private List<IMySlimBlock> m_blocksToHack = new List<IMySlimBlock>();
        private List<IMySlimBlock> m_potentialFirewallBlocks = new List<IMySlimBlock>();
        List<IMyLandingGear> m_gearList = new List<IMyLandingGear>();
        private IMyCubeGrid m_grid;
        private IMySlimBlock m_currentTarget;
        private List<IMyCubeGrid> m_gridGroup = new List<IMyCubeGrid>();

        private bool m_debug = false;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            if (m_debug) MyLog.Default.WriteLineAndConsole("Beginning hacking block init!");
            base.Init(objectBuilder);
            this.objectBuilder = objectBuilder;
            Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;

            if (Sync.IsServer)
            {
                Entity.NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
            }
                

            if (!(Entity is IMyFunctionalBlock))
                MyLog.Default.WriteLineAndConsole("WARNING: Hacking Computer Is Not An IMyFunctionalBlock!");

            m_functionalBlock = Entity as IMyFunctionalBlock;
            m_cubeBlock = m_functionalBlock as MyCubeBlock;

            InitPowerSystem();

            m_functionalBlock.AppendingCustomInfo += UpdateInfo;

            m_soundEmitter = new MyEntity3DSoundEmitter((MyEntity)Entity);
            m_soundPair = new MySoundPair("BlockTimerSignalB");
            m_grid = m_functionalBlock.CubeGrid;
            m_gridEntityId = ((MyEntity)m_functionalBlock.CubeGrid).EntityId;
            m_ownerId = m_functionalBlock.OwnerId;
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = true)
        {
            return objectBuilder;
        }

        public override void UpdateOnceBeforeFrame()
        {
            AddEventHandlers(m_grid);
        }

        public override void UpdateAfterSimulation()
        {
            //if (m_debug) MyLog.Default.WriteLineAndConsole("HackingBlock.UpdateAfterSimulation()");

            if (Sync.IsServer && m_countdown++ > m_lastSuccess + 50 && CurrentState == States.Success && m_blocksToHack.Count > 1)
            {
                CurrentState = States.Hacking;
                UpdateTerminal();
            }

            if (Sync.IsClient && CurrentState == States.Hacking)
            { // Yellow blinking effect while hacking
                float emissivity = (float)MathHelper.Clamp(0.5 * (1 + Math.Sin(2 * 3.14 * m_countdown * 8)), 0.0, 1.0);
                m_functionalBlock.SetEmissiveParts(Emissive, Color.OrangeRed * emissivity, emissivity);
            }
        }

        public override void UpdateAfterSimulation10()
        {
            //if (m_debug) MyLog.Default.WriteLineAndConsole("HackingBlock.UpdateAfterSimulation10()");
            if (m_quickUpdate)
            {
                m_quickUpdate = false;
                HackingLogic();
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            //if (m_debug) MyLog.Default.WriteLineAndConsole("HackingBlock.UpdateAfterSimulation100()");

            if (!Sync.IsServer) return;

            m_updateTimer++;
            m_resourceSink.Update();

            if (!m_quickUpdate) HackingLogic();
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

            if (CurrentState == States.NoEnemies)
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

        private void OnBlockOwnershipChanged(IMyCubeGrid grid)
        { // I wish Keen just returned the BLOCK on this event ... now we have to rescan the WHOLE GRID
            if (CurrentState == States.NoEnemies) CurrentState = States.Off;
        }

        private void AddEventHandlers(IMyCubeGrid grid)
        {
            if (grid == null) return;

            if (m_debug) MyLog.Default.WriteLineAndConsole($"Registering event handlers");

            grid.OnBlockAdded += OnBlockAdded;
            grid.OnBlockOwnershipChanged += OnBlockOwnershipChanged;
            grid.OnGridSplit += OnGridSplit;
        }

        private void RemoveEventHandlers(IMyCubeGrid grid)
        {
            if (grid == null)
                return;

            if (m_debug)
                VRage.Utils.MyLog.Default.WriteLineAndConsole($"Unregistering event handlers");

            grid.OnBlockAdded -= OnBlockAdded;
            grid.OnBlockOwnershipChanged -= OnBlockOwnershipChanged;
            grid.OnGridSplit -= OnGridSplit;
        }

        private void OnBlockAdded(IMySlimBlock block)
        {
            if (m_debug) MyLog.Default.WriteLineAndConsole($"HackingBlock.OnBlockAdded");
            MyAPIGateway.Parallel.Start(() =>
            {
                try
                {
                    if (IsFunctionalFirewall(block))
                        MyAPIGateway.Utilities.InvokeOnGameThread(() => m_currentTarget = block);

                    if (IsValidHackingTarget(block) && !m_blocksToHack.Contains(block))
                    {
                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                        {
                            m_blocksToHack.Add(block);
                            CurrentState = States.Hacking;
                        });

                        if (block.FatBlock != null && (block.FatBlock is IMyPistonBase || block.FatBlock is IMyMechanicalConnectionBlock))
                        {
                            if (m_debug) MyLog.Default.WriteLineAndConsole($"Piston or rotor added");

                            MyAPIGateway.Utilities.InvokeOnGameThread(() => m_forceGridGroupUpdate = true);
                        }
                    }
                }
                catch (Exception e)
                {
                    MyLog.Default.WriteLineAndConsole($"Exception in HackingBlock.OnBlockAdded:\n{e.ToString()}");
                }
            });
        }

        private void RefreshAttributes()
        {
            if (((MyEntity)m_functionalBlock.CubeGrid).EntityId != m_gridEntityId)
            {
                RemoveEventHandlers(m_grid);
                m_grid = m_functionalBlock.CubeGrid;
                AddEventHandlers(m_grid);
                m_gridEntityId = ((MyEntity)m_functionalBlock.CubeGrid).EntityId;
            }

            m_ownerId = m_functionalBlock.OwnerId;
            m_potentialFirewallBlocks.Clear();
            m_blocksToHack.Clear();
            m_currentTarget = null;            
            UpdateTerminal();
        }

        private void GetGridGroup()
        {
            List<IMyCubeGrid> newGroup = new List<IMyCubeGrid>(MyAPIGateway.GridGroups.GetGroup(m_functionalBlock.CubeGrid, GridLinkTypeEnum.Physical));
            List<IMyCubeGrid> LandingGearGroup = new List<IMyCubeGrid>(MyAPIGateway.GridGroups.GetGroup(m_functionalBlock.CubeGrid, GridLinkTypeEnum.NoContactDamage));

            foreach (var grid in LandingGearGroup)
                if (!newGroup.Contains(grid))
                    newGroup.Add(grid);

            foreach (var grid in m_gridGroup)
                RemoveEventHandlers(grid);

            m_gridGroup = newGroup;

            foreach (var grid in m_gridGroup)
                AddEventHandlers(grid);
        }

        private void OnGridSplit(IMyCubeGrid grid1, IMyCubeGrid grid2)
        {
            RefreshAttributes();
            if (m_debug) MyVisualScriptLogicProvider.SendChatMessage("GRID SPLIT DETECTED!", "Debug", 0L, "Red");
        }

        private bool IsFunctionalFirewall(IMySlimBlock block)
        {
            try
            {
                if (block != null && block.BlockDefinition.Id.SubtypeId.String == "LargeFirewallBlock")
                {
                    MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                    { // Adds the firewall to a will-check list even if it's not powered
                        if (block != null && !m_potentialFirewallBlocks.Contains(block))
                        {
                            if (m_debug) MyVisualScriptLogicProvider.SendChatMessage("Firewall block was added to m_potentialFirewallBlocks", "Debug", 0L, "Red");
                                
                            m_potentialFirewallBlocks.Add(block);
                        }
                    });

                    if (block.FatBlock == null || block.FatBlock.GameLogic == null)
                        return false;
                    
                    FirewallBlock firewallLogic = block.FatBlock.GameLogic.GetAs<FirewallBlock>();

                    if (firewallLogic != null && firewallLogic.IsWorking())
                        return true;
                }
                return false;
            }
            catch (Exception e)
            {
                VRage.Utils.MyLog.Default.WriteLineAndConsole($"Exception in IsFunctionalFirewall:\n{e.ToString()}");
                return false;
            }
        }

        public override void Close()
        {
            if (m_debug) MyLog.Default.WriteLineAndConsole("Hackingblock.Close()!");
            m_functionalBlock.AppendingCustomInfo -= UpdateInfo;

            RemoveEventHandlers(m_grid);

            if (m_soundEmitter != null)
                m_soundEmitter.StopSound(true);

            base.Close();
        }

        private void UpdateTerminal()
        {
            if (m_currentTarget != null)
                TargetId = m_currentTarget.FatBlock.EntityId;

            HackingComputerSync.SendHackingBlockStates(this);
        }

        void ClearTargets(Exception e = null)
        {
            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            {
                m_blocksToHack.Clear();
                m_currentTarget = null;
                Attempts = 0;
                m_potentialFirewallBlocks.Clear();
                UpdateTerminal();
            });

            if (e != null)
                MyLog.Default.WriteLineAndConsole($"Exception in HackingBlock.UpdateAfterSimulation100. Clearing blocksToHack list:\n{e.ToString()}");
        }

        private void HandleLockModeChanged(IMyLandingGear gear)
        {
            MyAPIGateway.Parallel.Start(() =>
            {
                try
                {
                    if (gear.LockMode != LandingGearMode.Locked)
                        return;

                    var entity = gear.GetAttachedEntity();

                    if (entity == null || !(entity is IMyCubeGrid))
                        return;

                    IMyCubeGrid grid = entity as IMyCubeGrid;

                    if (grid != null && !m_gridGroup.Contains(grid))
                    {
                        if (BlockIsInGridGroup(((IMyCubeBlock)gear).SlimBlock))
                            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                                { m_forceGridGroupUpdate = true; });

                        else
                            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                                { m_gearList.Remove(gear); });
                    }
                }
                catch
                {
                    MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                    { m_forceGridGroupUpdate = true; });
                }
            });
        }

        bool BlockIsInGridGroup(IMySlimBlock block)
        {
            foreach (var grid in m_gridGroup)
                if (block.CubeGrid == grid)
                    return true;

            return false;
        }

        void HandleLandingGear(IMySlimBlock block)
        {
            if (block.FatBlock == null || !(block.FatBlock is IMyLandingGear))
                return;

            IMyLandingGear gear = block.FatBlock as IMyLandingGear;

            if (gear != null && !m_gearList.Contains(gear))
                m_gearList.Add(gear);
        }

        private void ForceOffState()
        {
            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            {
                CurrentState = States.Off;
                m_currentTarget = null;
                TargetId = 0L;
                UpdateTerminal();
            });
        }

        /// <summary>
        /// Main logic. Heavy calculations are done in parallel.
        /// </summary>
        void HackingLogic()
        {
            MyAPIGateway.Parallel.Start(() =>
            {
                try
                {
                    // Check if the grid was merged, split, or if ownership changes (lost a hack battle)
                    if (m_gridEntityId != ((MyEntity)m_functionalBlock.CubeGrid).EntityId || m_ownerId != m_functionalBlock.OwnerId)
                    {
                        MyAPIGateway.Utilities.InvokeOnGameThread(() => { RefreshAttributes(); });
                        return;
                    }

                    // Check various conditions that might prevent hacking
                    if (m_functionalBlock.OwnerId == 0 || !m_powerAvailable || !m_functionalBlock.Enabled || !m_cubeBlock.IsFunctional)
                    {
                        ForceOffState();
                        return;
                    }

                    // Check if the hacking block is in a safezone
                    IMyEntity hackingBlockEntity = m_functionalBlock as IMyEntity;
                    foreach (MySafeZone safeZone in MySessionComponentSafeZones.SafeZones)
                    {
                        if (hackingBlockEntity == null || safeZone == null)
                            break;

                        if (!safeZone.Enabled)
                            continue;

                        // Check if the hacking block intersects with the safe zone's shape
                        if (safeZone.Shape == MySafeZoneShape.Box)
                        {
                            BoundingBoxD box = safeZone.PositionComp.WorldAABB;
                            if (hackingBlockEntity.GetIntersectionWithAABB(ref box))
                            {
                                ForceOffState();
                                return;
                            }
                        }
                        else if (safeZone.Shape == MySafeZoneShape.Sphere)
                        {
                            BoundingSphereD sphere = new BoundingSphereD(safeZone.PositionComp.GetPosition(), safeZone.Radius);
                            if (hackingBlockEntity.GetIntersectionWithSphere(ref sphere))
                            {
                                ForceOffState();
                                return;
                            }
                        }
                    }

                    // Force grid group update if needed
                    if (m_forceGridGroupUpdate)
                    {
                        ClearTargets();
                        MyAPIGateway.Utilities.InvokeOnGameThread(() => { m_forceGridGroupUpdate = false; });
                        return;
                    }

                    // If no hacking targets, attempt to find new ones
                    if ((m_blocksToHack.Count < 1 && CurrentState != States.NoEnemies)
                        || (CurrentState == States.NoEnemies && m_updateTimer > m_lastupdate + 36))
                    {
                        GetGridGroup();
                        foreach (var grid in m_gridGroup)
                            grid.GetBlocks(m_blocksToHack, IsValidHackingTarget);
                        m_lastupdate = m_updateTimer;
                    }

                    // Handle lock mode changes of landing gears
                    foreach (var gear in m_gearList)
                        HandleLockModeChanged(gear);

                    // If no hacking targets, update state and return
                    if (m_blocksToHack.Count < 1)
                    {
                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                        {
                            TargetId = 0L;
                            CurrentState = States.NoEnemies;
                            UpdateTerminal();
                        });
                        return;
                    }

                    // Acquire a new hacking target if current target is null
                    if (m_currentTarget == null)
                    {
                        AcquireNewTarget();
                        return;
                    }
                    // Check existing firewalls for repairs/ownership change
                    else if (!IsFunctionalFirewall(m_currentTarget))
                        CheckExistingFirewalls();

                    // Determine hacking chance based on computer count
                    Chance = GetComputerCount(m_currentTarget);

                    // Assign hacking chance based on computer count
                    if (Chance < 100)
                    {
                        Chance = 2;
                    }
                    else if (Chance <= 500)
                    {
                        Chance = 4;
                    }
                    else if (Chance <= 750)
                    {
                        Chance = 8;
                    }
                    else if (Chance < 1000)
                    {
                        Chance = 20;
                    }
                    else
                    {
                        Chance = 25;
                    }

                    // Check if current target is valid
                    if (!CurrentTargetIsValid())
                        return;

                    // Attempt successful hack if chance is 1
                    if (Chance == 1)
                    {
                        CurrentState = States.Success;
                        ProcessSuccessfulHack();
                    }
                    // If chance is met, attempt successful hack
                    else if (m_random.Next(Chance) % Chance == 0)
                        ProcessSuccessfulHack();
                    // If chance is not met, attempt hacking again
                    else
                        MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                        {
                            CurrentState = States.Hacking;
                            UpdateTerminal();
                        });
                }
                catch (Exception e)
                {
                    ClearTargets(e);
                }
            });
        }


        private void AcquireNewTarget()
        {
            Attempts = 0;
            int targetIndex = m_random.Next(m_blocksToHack.Count);

            for (int i = 0; i < m_blocksToHack.Count; i++)
                if (IsFunctionalFirewall(m_blocksToHack[i]))
                    targetIndex = i;

            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            {
                try
                {
                    if (m_blocksToHack[targetIndex] != null)
                    {
                        m_currentTarget = m_blocksToHack[targetIndex];
                        TargetId = m_currentTarget.FatBlock.EntityId;
                    }
                    else
                        m_blocksToHack.RemoveAt(targetIndex);
                }
                catch (Exception e)
                    { ClearTargets(e); }
            });
        }
        
        private void CheckExistingFirewalls()
        { // Changes current target to a firewall if a valid one exists
            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            { // I know this looks like a mess, but it will prevent InvalidOperationException
                try
                {
                    foreach (var firewall in m_potentialFirewallBlocks)
                        MyAPIGateway.Parallel.Start(() =>
                        {
                            if (IsFunctionalFirewall(firewall) && IsValidHackingTarget(firewall))
                                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                                { m_currentTarget = firewall; });
                        });
                }
                catch (Exception e)
                {
                    VRage.Utils.MyLog.Default.WriteLineAndConsole($"Exception in HackingBlock.CheckExistingFirewalls:\n{e.ToString()}");
                }
            });
        }
        
        private void ProcessSuccessfulHack()
        {
            MyAPIGateway.Utilities.InvokeOnGameThread(() =>
            { 
                try
                {
                    if (m_currentTarget != null && m_currentTarget.FatBlock != null)
                    {
                        (m_currentTarget.FatBlock as MyCubeBlock).ChangeOwner(0, MyOwnershipShareModeEnum.Faction);
                        (m_currentTarget.FatBlock as MyCubeBlock).ChangeBlockOwnerRequest(m_ownerId, MyOwnershipShareModeEnum.Faction);
                    }
                    CurrentState = States.Success;
                    m_lastSuccess = m_countdown;
                    m_blocksToHack.Remove(m_currentTarget);
                    m_currentTarget = null;
                    UpdateTerminal();
                }
                catch (Exception e)
                {
                    VRage.Utils.MyLog.Default.WriteLineAndConsole($"Exception in HackingBlock.ProcessSuccessfulHack:\n{e.ToString()}");
                    ClearTargets();
                }
            });
        }

        private bool CurrentTargetIsValid()
        {
            try
            {
                if (!IsValidHackingTarget(m_currentTarget))
                {
                    MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                    {
                        if (!IsValidHackingTarget(m_currentTarget))
                        { // we have to check again because m_currentTarget might have changed on the game thread
                            m_blocksToHack.Remove(m_currentTarget);
                            CurrentState = States.Success;
                            m_currentTarget = null;
                            UpdateTerminal();
                            m_quickUpdate = true; // Force another update to loop through invalid targets faster.
                        }
                    });
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                ClearTargets();
                VRage.Utils.MyLog.Default.WriteLineAndConsole($"Exception in HackingBlock.CurrentTargetIsValid:\n{e.ToString()}");
                return false;
            }
        }

        private bool IsValidHackingTarget(IMySlimBlock block)
        {
            try
            {
                if (block == null || block.FatBlock == null)
                    return false;

				var grid2 = block.CubeGrid as MyCubeGrid;

				if (grid2 == null || !grid2.Editable || !grid2.DestructibleBlocks)
					return false;

                HandleLandingGear(block);

                if (!block.FatBlock.IsWorking || !block.FatBlock.IsFunctional)
                    return false;
                    
                if (!MyRelationsBetweenPlayerAndBlockExtensions.IsFriendly(block.FatBlock.GetUserRelationToOwner(m_functionalBlock.OwnerId)))
                {
                    if (block.CubeGrid == (IMyCubeGrid)m_functionalBlock.CubeGrid)
                        return true;

                    foreach (var grid in m_gridGroup)
                        if (block.CubeGrid == grid)
                        {
                            Chance *= 2; // Difficulty is doubled for subgrids
                            return true;
                        }
                }
                return false;
            }
            catch (Exception e)
            {
                ClearTargets();
                VRage.Utils.MyLog.Default.WriteLineAndConsole($"Exception in HackingBlock.IsValidHackingTarget:\n{e.ToString()}");
                return false;
            }
        }

        public void UpdateClient()
        {
            try
            {
                Color emissiveColor;

                switch (CurrentState)
                {
                    case States.Success:
                        emissiveColor = Color.Green;
                        if (m_soundEmitter != null)
                            m_soundEmitter.PlaySound(m_soundPair);
                        break;
                    case States.NewTarget:
                    case States.Hacking:
                        emissiveColor = Color.OrangeRed;
                        break;
                    case States.NoEnemies:
                        emissiveColor = Color.Cyan;
                        break;
                    case States.Off:
                    default:
                        emissiveColor = Color.Red;
                        break;
                }

                m_functionalBlock.SetEmissiveParts(Emissive, emissiveColor, 1f);
                m_functionalBlock.RefreshCustomInfo();

                if (Sync.IsClient && MyAPIGateway.Gui?.GetCurrentScreen == MyTerminalPageEnum.ControlPanel)
                { // Force terminal screen to refresh if being viewed
                    Tools.UpdateTerminalClient((MyCubeBlock)m_functionalBlock);
                }
            }
            catch (Exception e)
            {
                VRage.Utils.MyLog.Default.WriteLineAndConsole($"Exception in HackingBlock.UpdateClient:\n{e.ToString()}");
            }
        }

        private void UpdateInfo(IMyTerminalBlock arg1, StringBuilder arg2)
        {
            try
            {
                arg2.Append("- Hacking Computer v3.0 -\n\n");
                if (m_functionalBlock.OwnerId == 0L)
                {
                    arg2.Append("!!WARNING!!\n\nBlock Must Be Owned");
                    return;
                }

                if (CurrentState == States.NoEnemies)
                    arg2.Append("No Enemy Blocks Found! \nSleeping until grid is modified ...");

                else if (CurrentState == States.NewTarget)
                    arg2.Append("Too many attempts failed. Switching targets...");

                else if (CurrentState == States.Hacking || CurrentState == States.Success)
                {
                    string targetName = "<<Unknown>>";
                    IMyEntity target;

                    if (MyAPIGateway.Entities.TryGetEntityById(TargetId, out target) && target is IMyTerminalBlock)
                        targetName = (target as IMyTerminalBlock).CustomName;

                    arg2.Append($"Attempting to hack {targetName}\n\n"
                      + $"Attempts: {Attempts}"
                      + $"\n\nChance of success is 1 in {Chance}...\n");

                    if (CurrentState == States.Hacking)
                        arg2.Append("    -- Last attempt: Failed!");
                    else if (CurrentState == States.Success)
                        arg2.Append("    -- Last attempt: SUCCESS!");
               }
            }
            catch(Exception e)
            {
                VRage.Utils.MyLog.Default.WriteLineAndConsole($"Exception in HackingBlock.updateInfo:\n{e.ToString()}");
            }
        }

        private int GetComputerCount(IMySlimBlock block)
        {
            var components = MyDefinitionManager.Static.GetCubeBlockDefinition(block.GetObjectBuilder()).Components;
            int computers = 0;

            for (var i = 0; i < components.Length; i++)
                if (components[i].Definition.Id.SubtypeName == "Computer")
                    computers += components[i].Count;

            return computers;
        }
    }
}