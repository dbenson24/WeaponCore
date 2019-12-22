﻿using SpaceEngineers.Game.ModAPI;
using VRageMath;
using WeaponCore.Platform;
using WeaponCore.Projectiles;
using WeaponCore.Support;
using static WeaponCore.Support.WeaponComponent.Start;
using static WeaponCore.Platform.Weapon.TerminalActionState;

namespace WeaponCore
{
    public partial class Session
    {
        private void AiLoop() //Fully Inlined due to keen's mod profiler
        {
            foreach (var aiPair in GridTargetingAIs)
            {
                ///
                ///
                /// GridAi update section
                ///
                ///
                
                var gridAi = aiPair.Value;
                if (!gridAi.GridInit || !gridAi.MyGrid.InScene || gridAi.MyGrid.MarkedForClose) 
                    continue;

                var dbIsStale = Tick - gridAi.TargetsUpdatedTick > 100;
                var readyToUpdate = dbIsStale && DbCallBackComplete && DbTask.IsComplete;
                if (readyToUpdate && gridAi.UpdateOwner())
                    gridAi.RequestDbUpdate();

                if (!gridAi.DeadProjectiles.IsEmpty) {
                    Projectile p;
                    while (gridAi.DeadProjectiles.TryDequeue(out p)) {
                        gridAi.LiveProjectile.Remove(p);
                    }
                    gridAi.LiveProjectileTick = Tick;
                }

                var weaponsInStandby = gridAi.ManualComps == 0 && !gridAi.CheckReload && gridAi.Gunners.Count == 0;
                if (!gridAi.DbReady && weaponsInStandby) 
                    continue;

                if (gridAi.HasPower || gridAi.HadPower || gridAi.UpdatePowerSources || Tick180) 
                    gridAi.UpdateGridPower();
                
                if (!gridAi.HasPower) continue;

                ///
                ///
                /// Comp update section
                ///
                ///

                for (int i = 0; i < gridAi.Weapons.Count; i++)
                {
                    var comp = gridAi.Weapons[i];
                    if (comp.Platform.State != MyWeaponPlatform.PlatformState.Ready)
                        continue;

                    if (!comp.State.Value.Online || comp.Status != Started) {

                        if (comp.Status != Started) 
                            comp.HealthCheck();

                        continue;
                    }
                    ///
                    ///
                    /// Weapon update section
                    ///
                    ///

                    for (int j = 0; j < comp.Platform.Weapons.Length; j++) {

                        var w = comp.Platform.Weapons[j];
                        var lastGunner = comp.Gunner;
                        var gunner = comp.Gunner = comp.MyCube == ControlledEntity;

                        if (!comp.Set.Value.Weapons[w.WeaponId].Enable) 
                            continue;

                        ///
                        /// Check target for expire states
                        /// 
                        
                        w.TargetWasExpired = w.Target.Expired;
                        if (!w.Target.Expired) {

                            if (w.Target.Entity == null && w.Target.Projectile == null) {

                                w.Target.Reset();
                            }
                            else if (w.Target.Entity != null && w.Target.Entity.MarkedForClose) {

                                w.Target.Reset();
                            }
                            else if (w.Target.Projectile != null && (!gridAi.LiveProjectile.Contains(w.Target.Projectile) || w.Target.IsProjectile && w.Target.Projectile.State != Projectile.ProjectileState.Alive)) {

                                w.Target.Reset();
                            }
                            else if (w.TrackingAi) {

                                if (!Weapon.TrackingTarget(w, w.Target, !gunner)) {

                                    w.Target.Reset();
                                }
                            }
                            else {

                                Vector3D targetPos;
                                if (w.IsTurret) {

                                    if (!w.TrackTarget) {

                                        if ((comp.TrackingWeapon.Target.Projectile != w.Target.Projectile || w.Target.IsProjectile && w.Target.Projectile.State != Projectile.ProjectileState.Alive || comp.TrackingWeapon.Target.Entity != w.Target.Entity)) {

                                            w.Target.Reset();
                                        }
                                    }
                                    else if (!Weapon.TargetAligned(w, w.Target, out targetPos)) {

                                        w.Target.Reset();
                                    }
                                }
                                else if (w.TrackTarget && !Weapon.TargetAligned(w, w.Target, out targetPos)) {

                                    w.Target.Reset();
                                }
                            }
                        }

                        var targetChange = w.TargetWasExpired != w.Target.Expired;

                        if (gunner && UiInput.MouseButtonPressed)
                            w.TargetPos = Vector3D.Zero;

                        ///
                        /// Set weapon Ai state
                        /// 

                        if (w.DelayCeaseFire) {

                            if (gunner || !w.AiReady || w.DelayFireCount++ > w.System.TimeToCeaseFire) {

                                w.DelayFireCount = 0;
                                w.AiReady = gunner || !w.Target.Expired && ((w.TrackingAi || !w.TrackTarget) && w.Target.TargetLock) || !w.TrackingAi && w.TrackTarget && !w.Target.Expired;
                            }
                        }
                        else {

                            w.AiReady = gunner || !w.Target.Expired && ((w.TrackingAi || !w.TrackTarget) && w.Target.TargetLock) || !w.TrackingAi && w.TrackTarget && !w.Target.Expired;
                        }

                        if (targetChange) {

                            w.EventTriggerStateChanged(Weapon.EventTriggers.Tracking, !w.Target.Expired);
                            w.EventTriggerStateChanged(Weapon.EventTriggers.StopTracking, w.Target.Expired);

                            if (w.Target.Expired)
                                w.TargetReset = true;
                        }


                        w.SeekTarget = w.Target.Expired && w.TrackTarget;
                        if (w.SeekTarget) AcquireTargets.Enqueue(w);

                        ///
                        /// Check weapon's turret to see if its time to go home
                        /// 

                        var wState = comp.State.Value.Weapons[w.WeaponId];
                        if (w.TurretMode) {

                            if (comp.State.Value.Online) {
                                
                                if (targetChange && w.Target.Expired || gunner != lastGunner && !gunner) 
                                    FutureEvents.Schedule(w.HomeTurret, null, 240);

                                if (gunner != lastGunner && gunner) {

                                    gridAi.ManualComps++;
                                    comp.Shooting++;
                                }
                                else if (gunner != lastGunner && !gunner) {

                                    gridAi.ManualComps = gridAi.ManualComps - 1 > 0 ? gridAi.ManualComps - 1 : 0;
                                    comp.Shooting = comp.Shooting - 1 > 0 ? comp.Shooting - 1 : 0;
                                }
                            }
                        }

                        // reload if needed
                        if (gridAi.CheckReload && w.System.AmmoDefId == gridAi.NewAmmoType) 
                            ComputeStorage(w);

                        if (comp.Debug) 
                            WeaponDebug(w);

                        ///
                        /// Determine if its time to shoot
                        ///
                        
                        var reloading = !w.System.EnergyAmmo && w.Reloading;

                        if (!comp.Overheated && !reloading && !w.System.DesignatorWeapon && (wState.ManualShoot == ShootOn || wState.ManualShoot == ShootOnce || (wState.ManualShoot == ShootOff && w.AiReady && !comp.Gunner) || ((wState.ManualShoot == ShootClick || comp.Gunner) && !gridAi.SupressMouseShoot && (j == 0 && UiInput.MouseButtonLeft || j == 1 && UiInput.MouseButtonRight))))
                        {
                            if (gridAi.AvailablePowerChange)
                                w.DelayTicks = 0;

                            var targetRequested = w.SeekTarget && targetChange;
                            if (!targetRequested && (w.DelayTicks == 0 || w.ChargeUntilTick <= Tick))
                            {
                                if (!w.DrawingPower)
                                {
                                    gridAi.RequestedWeaponsDraw += w.RequiredPower;
                                    w.DrawingPower = true;
                                }
                                ShootingWeapons.Enqueue(w);
                            }
                            else if (w.ChargeUntilTick > Tick)
                                w.Charging = true;
                        }
                        else if (w.IsShooting)
                            w.StopShooting();
                    }
                }

                gridAi.OverPowered = gridAi.RequestedWeaponsDraw > 0 && gridAi.RequestedWeaponsDraw > gridAi.GridMaxPower;
                gridAi.CheckReload = false;
                gridAi.AvailablePowerChange = false;
            }

            if (DbCallBackComplete && DbsToUpdate.Count > 0 && DbTask.IsComplete) 
                UpdateDbsInQueue();
        }

        private void CheckAcquire()
        {
            while (AcquireTargets.Count > 0)
            {
                var w = AcquireTargets.Dequeue();
                var gridAi = w.Comp.Ai;
                var comp = w.Comp;

                if ((w.Target.Expired && w.TrackTarget) || gridAi.TargetResetTick == Tick)
                {
                    if (!w.SleepTargets || Tick - w.TargetCheckTick > 119 || gridAi.TargetResetTick == Tick || w.TargetReset)
                    {

                        w.TargetReset = false;
                        if (comp.TrackingWeapon != null && comp.TrackingWeapon.System.DesignatorWeapon && comp.TrackingWeapon != w && !comp.TrackingWeapon.Target.Expired)
                        {
                            GridAi.AcquireTarget(w, false, comp.TrackingWeapon.Target.Entity.GetTopMostParent());
                        }
                        else
                        {
                            GridAi.AcquireTarget(w, gridAi.TargetResetTick == Tick);
                        }
                    }
                }
                else if (w.IsTurret && !w.TrackTarget && w.Target.Expired)
                {
                    w.Target = w.Comp.TrackingWeapon.Target;
                }
            }
        }

        private void ShootWeapons() 
        {
            while (ShootingWeapons.Count > 0)
            {
                var w = ShootingWeapons.Dequeue();
                //TODO add logic for power priority
                if (w.Comp.Ai.OverPowered && (w.System.EnergyAmmo || w.System.IsHybrid)) {

                    if (w.DelayTicks == 0) {

                        var percUseable = w.RequiredPower / w.Comp.Ai.RequestedWeaponsDraw;
                        var oldUseable = w.UseablePower;
                        w.UseablePower = (w.Comp.Ai.GridMaxPower * .98f) * percUseable;
                        if (w.IsShooting) {
                            w.Comp.SinkPower = (w.Comp.SinkPower - oldUseable) + w.UseablePower;
                            w.Comp.MyCube.ResourceSink.Update();
                        }

                        w.DelayTicks = 1 + ((uint)(w.RequiredPower - w.UseablePower) * 20); //arbitrary charge rate ticks/watt should be config

                        w.ChargeUntilTick = Tick + w.DelayTicks;
                        w.Charging = true;
                    }
                    else if (w.ChargeUntilTick <= Tick) {

                        w.Charging = false;
                        w.ChargeUntilTick = Tick + w.DelayTicks;
                    }
                    w.Comp.TerminalRefresh();
                }
                else if(w.RequiredPower - w.UseablePower > 0.0001) {

                    var oldUseable = w.UseablePower;
                    w.UseablePower = w.RequiredPower;
                    w.Comp.SinkPower = (w.Comp.SinkPower - oldUseable) + w.UseablePower;
                    w.DelayTicks = 0;
                    w.Charging = false;
                }

                if (w.Charging)
                    continue;

                w.Shoot();
                
                if (w.AvCapable && w.BarrelAvUpdater.Reader.Count > 0) 
                    w.ShootGraphics();
            }
        }
    }
}