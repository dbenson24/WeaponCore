using System;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRageMath;
using VRageRender.Messages;
using WeaponCore.Support;
using static Sandbox.Definitions.MyDefinitionManager;

namespace WeaponCore
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation | MyUpdateOrder.AfterSimulation | MyUpdateOrder.Simulation, int.MaxValue - 1)]
    public partial class Session : MySessionComponentBase
    {
        public override void BeforeStart()
        {
            try
            {
                if (!SupressLoad)
                    BeforeStartInit();
            }
            catch (Exception ex) { Log.Line($"Exception in BeforeStart: {ex}"); }
        }

        public override void UpdatingStopped()
        {
            try
            {
                if (!SupressLoad)
                    Paused();

            }
            catch (Exception ex) { Log.Line($"Exception in UpdatingStopped: {ex}"); }
        }


        public override void UpdateBeforeSimulation()
        {
            try
            {
                if (SupressLoad)
                    return;

                if (DeformProtection.Count > 0 && Tick - LastDeform > 0)
                    DeformProtection.Clear();
                
                Timings();

                if (IsClient)  {
                    if (ClientSideErrorPkt.Count > 0)
                        ReproccessClientErrorPackets();

                    if (ClientPacketsToClean.Count > 0)
                        CleanClientPackets();
                }

                /*
                for (int i = DebugLines.Count - 1; i >= 0; i--)
                    if (!DebugLines[i].Draw(Tick))
                        DebugLines.RemoveAtFast(i);

                TotalAcquireChecks += AcquireChecks;

                if (AcquireChecks < LowAcquireChecks)
                    LowAcquireChecks = AcquireChecks;
                else if (AcquireChecks > HighAcquireChecks)
                    HighAcquireChecks = AcquireChecks;
                AcquireChecks = 0;
                if (Tick60)
                {
                    AverageAcquireChecks = TotalAcquireChecks / 60;
                    Log.Line($"Low:{LowAcquireChecks} - High:{HighAcquireChecks} - Average:{AverageAcquireChecks} - Awake:{AcqManager.WasAwake} - Asleep:{AcqManager.WasAsleep}");
                    TotalAcquireChecks = 0;
                    LowAcquireChecks = int.MaxValue;
                    HighAcquireChecks = int.MinValue;
                }
                */

                // Environment.CurrentManagedThreadId

                if (Tick60) AcqManager.UpdateAsleep();
                if (Tick600) AcqManager.ReorderSleep();

                if (!DedicatedServer && TerminalMon.Active)
                    TerminalMon.Monitor();

                MyCubeBlock cube;
                if (Tick60 && UiInput.ActionKeyPressed && UiInput.CtrlPressed && GetAimedAtBlock(out cube) && cube.BlockDefinition != null && WeaponCoreBlockDefs.ContainsKey(cube.BlockDefinition.Id.SubtypeName))
                    ProblemRep.GenerateReport(cube);

                if (!IsClient && !InventoryUpdate && (!WeaponToPullAmmo.Empty || !WeaponsToRemoveAmmo.Empty) && ITask.IsComplete)
                    StartAmmoTask();

                if (!CompsToStart.IsEmpty)
                    StartComps();

                if (Tick120 && CompsDelayed.Count > 0)
                    DelayedComps();

                if (Tick20 && !DelayedGridAiClean.IsEmpty)
                    DelayedGridAiCleanup();

                if (CompReAdds.Count > 0)
                    ChangeReAdds();

                if (Tick3600 && MpActive) 
                    NetReport();

                if (Tick180) 
                    ProfilePerformance();

                FutureEvents.Tick(Tick);

                if (HomingWeapons.Count > 0)
                    UpdateHomingWeapons();

                if (!DedicatedServer && ActiveControlBlock != null && !InMenu) Wheel.UpdatePosition();

                if (MpActive) {
                    if (PacketsToClient.Count > 0 || PrunedPacketsToClient.Count > 0)
                        ProccessServerPacketsForClients();
                    if (PacketsToServer.Count > 0)
                        ProccessClientPacketsForServer();
                }

            }
            catch (Exception ex) { Log.Line($"Exception in SessionBeforeSim: {ex}"); }
        }

        public override void Simulate()
        {
            try
            {
                if (SupressLoad)
                    return;

                if (!DedicatedServer) {
                    EntityControlUpdate();
                    CameraMatrix = Session.Camera.WorldMatrix;
                    CameraPos = CameraMatrix.Translation;
                    PlayerPos = Session.Player?.Character?.WorldAABB.Center ?? Vector3D.Zero;
                }

                if (GameLoaded) {
                    DsUtil.Start("ai");
                    AiLoop();
                    DsUtil.Complete("ai", true);


                    DsUtil.Start("charge");
                    if (ChargingWeapons.Count > 0) UpdateChargeWeapons();
                    DsUtil.Complete("charge", true);

                    DsUtil.Start("acquire");
                    if (AcquireTargets.Count > 0) CheckAcquire();
                    DsUtil.Complete("acquire", true);

                    DsUtil.Start("shoot");
                    if (ShootingWeapons.Count > 0) ShootWeapons();
                    DsUtil.Complete("shoot", true);

                }

                if (!DedicatedServer && !Wheel.WheelActive && !InMenu) {
                    UpdateLocalAiAndCockpit();
                    if (UiInput.PlayerCamera && ActiveCockPit != null) 
                        TargetSelection();
                }

                DsUtil.Start("ps");
                Projectiles.SpawnAndMove();
                DsUtil.Complete("ps", true);

                DsUtil.Start("pi");
                Projectiles.Intersect();
                DsUtil.Complete("pi", true);

                DsUtil.Start("pd");
                Projectiles.Damage();
                DsUtil.Complete("pd", true);

                DsUtil.Start("pa");
                Projectiles.AvUpdate();
                DsUtil.Complete("pa", true);

                DsUtil.Start("av");
                if (!DedicatedServer) Av.End();
                DsUtil.Complete("av", true);

                if (MpActive)  {
                    
                    DsUtil.Start("network1");
                    if (PacketsToClient.Count > 0 || PrunedPacketsToClient.Count > 0) 
                        ProccessServerPacketsForClients();
                    if (PacketsToServer.Count > 0) 
                        ProccessClientPacketsForServer();

                    DsUtil.Complete("network1", true);
                }

            }
            catch (Exception ex) { Log.Line($"Exception in SessionSim: {ex}"); }
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                if (SupressLoad)
                    return;

                if (Placer != null) UpdatePlacer();

                if(AnimationsToProcess.Count > 0 || ThreadedAnimations.Count > 0) ProcessAnimations();

                if (GridTask.IsComplete)
                    CheckDirtyGrids();
            }
            catch (Exception ex) { Log.Line($"Exception in SessionAfterSim: {ex}"); }
        }

        public override void Draw()
        {
            try
            {
                if (SupressLoad || DedicatedServer || _lastDrawTick == Tick || _paused) return;
                _lastDrawTick = Tick;
                DsUtil.Start("draw");

                CameraMatrix = Session.Camera.WorldMatrix;
                CameraPos = CameraMatrix.Translation;
                CameraFrustrum.Matrix = (Camera.ViewMatrix * Camera.ProjectionMatrix);
                ScaleFov = Math.Tan(Camera.FovWithZoom * 0.5);

                if (HudUi.TexturesToAdd > 0) HudUi.DrawTextures();

                if ((UiInput.PlayerCamera || UiInput.FirstPersonView || InGridAiBlock) && !InMenu && !Session.Config.MinimalHud && !MyAPIGateway.Gui.IsCursorVisible)
                {
                    if (Wheel.WheelActive) Wheel.DrawWheel();
                    TargetUi.DrawTargetUi();
                }
                Av.Run();
                DsUtil.Complete("draw", true);
            }
            catch (Exception ex) { Log.Line($"Exception in SessionDraw: {ex}"); }
        }


        public override void HandleInput()
        {
            if (HandlesInput && !SupressLoad) {

                UiInput.UpdateInputState();
                if (MpActive)  {

                    if (UiInput.InputChanged && ActiveControlBlock != null) 
                        SendMouseUpdate(TrackingAi, ActiveControlBlock);
                    
                    if (TrackingAi != null && TargetUi.DrawReticle)  {
                        var dummyTarget = PlayerDummyTargets[PlayerId];
                        if (dummyTarget.LastUpdateTick == Tick)
                            SendFakeTargetUpdate(TrackingAi, dummyTarget);
                    }

                    if (PacketsToServer.Count > 0)
                        ProccessClientPacketsForServer();
                }
            }
        }

        public override void LoadData()
        {
            try
            {
                foreach (var mod in Session.Mods)
                {
                    if (mod.PublishedFileId == 1365616918) ShieldMod = true;
                    else if (mod.GetPath().Contains("AppData\\Roaming\\SpaceEngineers\\Mods\\DefenseShields"))
                        ShieldMod = true;
                    else if (mod.PublishedFileId == 1931509062 || mod.PublishedFileId == 1995197719 || mod.PublishedFileId == 2006751214 || mod.PublishedFileId == 2015560129)
                        ReplaceVanilla = true;
                    else if (mod.GetPath().Contains("AppData\\Roaming\\SpaceEngineers\\Mods\\VanillaReplacement"))
                        ReplaceVanilla = true;
                    else if (mod.PublishedFileId == 2123506303)
                    {
                        if (mod.Name != ModContext.ModId)
                            SupressLoad = true;
                    }
                }

                if (SupressLoad)
                    return;

                AllDefinitions = Static.GetAllDefinitions();
                SoundDefinitions = Static.GetSoundDefinitions();
                MyEntities.OnEntityCreate += OnEntityCreate;
                MyAPIGateway.Gui.GuiControlCreated += MenuOpened;
                MyAPIGateway.Gui.GuiControlRemoved += MenuClosed;

                MyAPIGateway.Utilities.RegisterMessageHandler(7771, Handler);
                MyAPIGateway.Utilities.SendModMessage(7772, null);

                TriggerEntityModel = ModContext.ModPath + "\\Models\\Environment\\JumpNullField.mwm";
                TriggerEntityPool = new MyConcurrentPool<MyEntity>(0, TriggerEntityClear, 10000, TriggerEntityActivator);

                ReallyStupidKeenShit();
            }
            catch (Exception ex) { Log.Line($"Exception in LoadData: {ex}"); }
        }

        protected override void UnloadData()
        {
            try
            {
                if (SupressLoad)
                    return;

                if (!PTask.IsComplete)
                    PTask.Wait();

                if (!CTask.IsComplete)
                    CTask.Wait();

                if (!ITask.IsComplete)
                    ITask.Wait();

                if (IsServer || DedicatedServer)
                    MyAPIGateway.Multiplayer.UnregisterMessageHandler(ServerPacketId, ProccessServerPacket);
                else
                {
                    MyAPIGateway.Multiplayer.UnregisterMessageHandler(ClientPacketId, ClientReceivedPacket);
                    MyAPIGateway.Multiplayer.UnregisterMessageHandler(StringPacketId, StringReceived);
                }

                MyAPIGateway.Utilities.UnregisterMessageHandler(7771, Handler);

                MyAPIGateway.TerminalControls.CustomControlGetter -= CustomControlHandler;
                MyEntities.OnEntityCreate -= OnEntityCreate;
                MyAPIGateway.Gui.GuiControlCreated -= MenuOpened;
                MyAPIGateway.Gui.GuiControlRemoved -= MenuClosed;

                MyVisualScriptLogicProvider.PlayerDisconnected -= PlayerDisconnected;
                MyVisualScriptLogicProvider.PlayerRespawnRequest -= PlayerConnected;
                ApiServer.Unload();

                PurgeAll();

                Log.Line("Logging stopped.");
                Log.Close();
            }
            catch (Exception ex) { Log.Line($"Exception in UnloadData: {ex}"); }
        }
    }
}

