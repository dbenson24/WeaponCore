﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRageMath;
using WeaponCore.Support;
using static WeaponCore.Projectiles.Projectile;
namespace WeaponCore.Projectiles
{
    public partial class Projectiles
    {
        private const float StepConst = MyEngineConstants.PHYSICS_STEP_SIZE_IN_SECONDS;
        internal const int PoolCount = 16;
        internal readonly ConcurrentQueue<Projectile> Hits = new ConcurrentQueue<Projectile>();

        internal readonly MyConcurrentPool<Fragments>[] ShrapnelPool = new MyConcurrentPool<Fragments>[PoolCount];
        internal readonly MyConcurrentPool<Fragment>[] FragmentPool = new MyConcurrentPool<Fragment>[PoolCount];
        internal readonly List<Fragments>[] ShrapnelToSpawn = new List<Fragments>[PoolCount];

        internal readonly MyConcurrentPool<List<MyEntity>>[] CheckPool = new MyConcurrentPool<List<MyEntity>>[PoolCount];
        internal readonly ObjectsPool<Projectile>[] ProjectilePool = new ObjectsPool<Projectile>[PoolCount];
        internal readonly EntityPool<MyEntity>[][] EntityPool = new EntityPool<MyEntity>[PoolCount][];
        internal readonly MyConcurrentPool<HitEntity>[] HitEntityPool = new MyConcurrentPool<HitEntity>[PoolCount];
        internal readonly ObjectsPool<Trajectile>[] TrajectilePool = new ObjectsPool<Trajectile>[PoolCount];
        internal readonly List<Trajectile>[] DrawProjectiles = new List<Trajectile>[PoolCount];
        internal readonly MyConcurrentPool<List<Vector3I>> V3Pool = new MyConcurrentPool<List<Vector3I>>();

        internal readonly object[] Wait = new object[PoolCount];

        internal Projectiles()
        {
            for (int i = 0; i < Wait.Length; i++)
            {
                Wait[i] = new object();
                ShrapnelToSpawn[i] = new List<Fragments>(25);
                ShrapnelPool[i] = new MyConcurrentPool<Fragments>(25);
                FragmentPool[i] = new MyConcurrentPool<Fragment>(100);
                CheckPool[i] = new MyConcurrentPool<List<MyEntity>>(50);
                ProjectilePool[i] = new ObjectsPool<Projectile>(1250);
                HitEntityPool[i] = new MyConcurrentPool<HitEntity>(250);
                DrawProjectiles[i] = new List<Trajectile>(500);
                TrajectilePool[i] = new ObjectsPool<Trajectile>(500);
            }
        }

        internal static MyEntity EntityActivator(string model)
        {
            var ent = new MyEntity();
            ent.Init(null, model, null, null, null);
            ent.Render.CastShadows = false;
            ent.IsPreview = true;
            ent.Save = false;
            ent.SyncFlag = false;
            ent.NeedsWorldMatrix = false;
            ent.Flags |= EntityFlags.IsNotGamePrunningStructureObject;
            MyEntities.Add(ent);
            return ent;
        }

        internal void Update()
        {
            //MyAPIGateway.Parallel.For(0, Wait.Length, x => Process(x), 1);
            for (int i = 0; i < Wait.Length; i++) Process(i);
        }

        private void Process(int i)
        {
            var noAv = Session.Instance.DedicatedServer;
            var camera = Session.Instance.Session.Camera;
            var cameraPos = camera.Position;
            lock (Wait[i])
            {
                var modelClose = false;
                var pool = ProjectilePool[i];
                var entPool = EntityPool[i];
                var drawList = DrawProjectiles[i];
                var vtPool = TrajectilePool[i];
                var spawnShrapnel = ShrapnelToSpawn[i];

                if (spawnShrapnel.Count > 0) {
                    for (int j = 0; j < spawnShrapnel.Count; j++)
                        spawnShrapnel[j].Spawn(i);
                    spawnShrapnel.Clear();
                }

                foreach (var p in pool.Active)
                {
                    p.Age++;
                    switch (p.State)
                    {
                        case ProjectileState.Dead:
                            continue;
                        case ProjectileState.Start:
                            p.Start(noAv, i);
                            if (p.ModelState == EntityState.NoDraw)
                                modelClose = p.CloseModel(this, i);
                            break;
                        case ProjectileState.Ending:
                        case ProjectileState.OneAndDone:
                        case ProjectileState.Depleted:
                            if (p.State == ProjectileState.Depleted)
                                p.ProjectileClose(this, i);
                            if (p.ModelState != EntityState.Exists) p.Stop(this, i);
                            else
                            {
                                modelClose = p.CloseModel(this, i);
                                p.Stop(this, i);
                            }
                            continue;
                    }
                    p.LastPosition = p.Position;
                    var isProjectile = p.T.Target.IsProjectile;
                    if (isProjectile && !p.T.Ai.LiveProjectile.Contains(p.T.Target.Projectile))
                    {
                        p.T.Target.Reset();
                        isProjectile = false;
                        Log.Line("Test");
                    }

                    if (p.Guidance == AmmoTrajectory.GuidanceType.Smart)
                    {
                        Vector3D newVel;
                        if ((p.AccelLength <= 0 || Vector3D.DistanceSquared(p.Origin, p.Position) > p.SmartsDelayDistSqr))
                        {
                            var newChase = p.Age - p.ChaseAge > p.MaxChaseAge || p.PickTarget;
                            var validTarget = isProjectile || p.T.Target.Entity != null && !p.T.Target.Entity.MarkedForClose;

                            if (newChase && p.EndChase() || validTarget || p.ZombieLifeTime % 30 == 0 && GridAi.ReacquireTarget(p))
                            {
                                if (p.ZombieLifeTime > 0) p.UpdateZombie(true);
                                var targetPos = Vector3D.Zero;
                                if (isProjectile) targetPos = p.T.Target.Projectile.Position;
                                else if (p.T.Target.Entity != null) targetPos = p.T.Target.Entity.PositionComp.WorldAABB.Center;

                                if (p.T.System.TargetOffSet)
                                {
                                    if (p.Age - p.LastOffsetTime > 300)
                                    {
                                        double dist;
                                        Vector3D.DistanceSquared(ref p.Position, ref targetPos, out dist);
                                        if (dist < p.OffsetSqr && Vector3.Dot(p.Direction, p.Position - targetPos) > 0)
                                            p.OffSetTarget(out p.TargetOffSet);
                                    }
                                    targetPos += p.TargetOffSet;
                                }

                                var physics = p.T.Target.Entity?.Physics ?? p.T.Target.Entity?.Parent?.Physics;

                                if (!isProjectile && (physics == null || targetPos == Vector3D.Zero))
                                    p.PrevTargetPos = p.PredictedTargetPos;
                                else p.PrevTargetPos = targetPos;

                                var tVel = Vector3.Zero;
                                if (isProjectile) tVel = p.T.Target.Projectile.Velocity;
                                else if (physics != null) tVel = physics.LinearVelocity;

                                p.PrevTargetVel = tVel;
                            }
                            else p.UpdateZombie();

                            var commandedAccel = CalculateMissileIntercept(p.PrevTargetPos, p.PrevTargetVel, p.Position, p.Velocity, p.AccelPerSec, p.T.System.Values.Ammo.Trajectory.Smarts.Aggressiveness, p.T.System.Values.Ammo.Trajectory.Smarts.MaxLateralThrust);
                            newVel = p.Velocity + (commandedAccel * StepConst);
                            p.AccelDir = commandedAccel / p.AccelPerSec;
                        }
                        else newVel = p.Velocity += (p.Direction * p.AccelLength);

                        Vector3D.Normalize(ref p.Velocity, out p.Direction);
                        if (newVel.LengthSquared() > p.MaxSpeedSqr) newVel = p.Direction * p.MaxSpeed;

                        p.Velocity = newVel;

                        if (p.EnableAv && Vector3D.Dot(p.VisualDir, p.AccelDir) < Session.VisDirToleranceCosine)
                        {
                            p.VisualStep += 0.0025;
                            if (p.VisualStep > 1) p.VisualStep = 1;

                            Vector3D lerpDir;
                            Vector3D.Lerp(ref p.VisualDir, ref p.AccelDir, p.VisualStep, out lerpDir);
                            Vector3D.Normalize(ref lerpDir, out p.VisualDir);
                        }
                        else if (p.EnableAv && Vector3D.Dot(p.VisualDir, p.AccelDir) >= Session.VisDirToleranceCosine)
                        {
                            p.VisualDir = p.AccelDir;
                            p.VisualStep = 0;
                        }
                    }
                    else if (p.AccelLength > 0)
                    {
                        var newVel = p.Velocity + p.AccelVelocity;
                        if (newVel.LengthSquared() > p.MaxSpeedSqr) newVel = p.Direction * p.MaxSpeed;
                        p.Velocity = newVel;
                    }

                    if (p.State == ProjectileState.OneAndDone)
                    {
                        var beamEnd = p.Position + (p.Direction * p.MaxTrajectory);
                        p.TravelMagnitude = p.Position - beamEnd;
                        p.Position = beamEnd;
                    }
                    else
                    {
                        p.TravelMagnitude = p.Velocity * StepConst;
                        p.Position += p.TravelMagnitude;
                    }
                    p.DistanceTraveled += Vector3D.Dot(p.Direction, p.Velocity * StepConst);

                    if (p.ModelState == EntityState.Exists)
                    {
                        p.T.EntityMatrix = MatrixD.CreateWorld(p.Position, p.VisualDir, MatrixD.Identity.Up);
                        if (p.EnableAv && p.AmmoEffect != null && p.T.System.AmmoParticle)
                        {
                            var offVec = p.Position + Vector3D.Rotate(p.T.System.Values.Graphics.Particles.Ammo.Offset, p.T.EntityMatrix);
                            p.AmmoEffect.WorldMatrix = p.T.EntityMatrix;
                            p.AmmoEffect.SetTranslation(offVec);
                        }
                    }
                    else if (!p.ConstantSpeed && p.EnableAv && p.AmmoEffect != null && p.T.System.AmmoParticle)
                        p.AmmoEffect.Velocity = p.Velocity;

                    if (p.State != ProjectileState.OneAndDone)
                    {
                        if (p.DistanceTraveled * p.DistanceTraveled >= p.DistanceToTravelSqr)
                        {
                            Die(p, i);
                            continue;
                        }
                    }

                    if (!isProjectile)
                    {
                        if (!p.T.System.VirtualBeams || p.T.MuzzleId == -1)
                        {
                            var beam = new LineD(p.LastPosition, p.Position);
                            MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref beam, p.SegmentList);
                            var segCount = p.SegmentList.Count;
                            if (segCount > 1 || segCount == 1 && p.SegmentList[0].Element != p.T.Ai.MyGrid)
                            {
                                var nearestHitEnt = GetAllEntitiesInLine(p, beam, p.SegmentList, i);
                                if (nearestHitEnt != null && Intersected(p, drawList, nearestHitEnt)) continue;
                                p.T.HitList.Clear();
                            }

                            if (p.T.MuzzleId == -1)
                            {
                                CreateFakeBeams(p, null, drawList, true);
                                continue;
                            }
                        }
                        else if (p.T.WeaponCache.VirtualHit && p.T.WeaponCache.HitEntity != null)
                        {
                            Intersected(p, drawList, p.T.WeaponCache.HitEntity);
                            continue;
                        }
                    }
                    else
                    {
                        try
                        {
                            var beam = new RayD(p.LastPosition, p.Direction);
                            var targetPos = p.T.Target.Projectile.Position;
                            var sphere = new BoundingSphereD(targetPos, 2.5f);
                            if (sphere.Intersects(beam) != null)
                            {
                                var hitEntity = HitEntityPool[i].Get();
                                hitEntity.Clean();
                                hitEntity.EventType = HitEntity.Type.Projectile;
                                hitEntity.Hit = true;
                                hitEntity.Projectile = p.T.Target.Projectile;
                                hitEntity.HitPos = targetPos;
                                hitEntity.Beam = new LineD(beam.Position, targetPos);
                                p.T.HitList.Add(hitEntity);

                                Intersected(p, drawList, hitEntity);
                                continue;
                            }
                        }
                        catch (Exception ex) { Log.Line($"Exception in ProjectileHit: {ex}"); }
                    }

                    if (!p.EnableAv) continue;

                    if (p.T.System.AmmoParticle)
                    {
                        p.TestSphere.Center = p.Position;
                        if (camera.IsInFrustum(ref p.TestSphere))
                        {
                            if ((p.ParticleStopped || p.ParticleLateStart))
                                p.PlayAmmoParticle();
                        }
                        else if (!p.ParticleStopped && p.AmmoEffect != null)
                            p.DisposeAmmoEffect(false,true);
                    }

                    if (p.HasTravelSound)
                    {
                        if (!p.AmmoSound)
                        {
                            double dist;
                            Vector3D.DistanceSquared(ref p.Position, ref cameraPos, out dist);
                            if (dist <= p.AmmoTravelSoundRangeSqr) p.AmmoSoundStart();
                        }
                        else p.TravelEmitter.SetPosition(p.Position);
                    }

                    if (p.DrawLine)
                    {
                        if (p.Grow)
                        {
                            if (p.AccelLength <= 0 && p.GrowStep++ >= p.T.ReSizeSteps) p.Grow = false;
                            else if (Vector3D.DistanceSquared(p.Origin, p.Position) > p.LineLength * p.LineLength) p.Grow = false;
                            p.T.UpdateShape(p.Position + -(p.Direction * p.DistanceTraveled), p.Position, p.Direction, p.DistanceTraveled);
                        }
                        else if (p.State == ProjectileState.OneAndDone)
                            p.T.UpdateShape(p.LastPosition, p.Position, p.Direction, p.MaxTrajectory);
                        else
                        {
                            var pointDir = p.Guidance == AmmoTrajectory.GuidanceType.Smart ? p.VisualDir : p.Direction;
                            p.T.UpdateShape(p.Position + -(pointDir * p.LineLength), p.Position, pointDir, p.LineLength);
                        }

                    }

                    p.T.OnScreen = false;
                    if (p.ModelState == EntityState.Exists)
                    {
                        p.ModelSphereLast.Center = p.LastEntityPos;
                        p.ModelSphereCurrent.Center = p.Position;
                        if (camera.IsInFrustum(ref p.ModelSphereLast) || camera.IsInFrustum(ref p.ModelSphereCurrent) || p.FirstOffScreen)
                        {
                            p.T.OnScreen = true;
                            p.FirstOffScreen = false;
                            p.LastEntityPos = p.Position;
                        }
                    }

                    if (!p.T.OnScreen && p.DrawLine)
                    {
                        var bb = new BoundingBoxD(Vector3D.Min(p.T.PrevPosition, p.T.Position), Vector3D.Max(p.T.PrevPosition, p.T.Position));
                        if (camera.IsInFrustum(ref bb)) p.T.OnScreen = true;
                    }

                    if (p.T.OnScreen)
                    {
                        p.T.Complete(null, false);
                        drawList.Add(p.T);
                    }
                }

                if (modelClose)
                    foreach (var e in entPool)
                        e.DeallocateAllMarked();

                vtPool.DeallocateAllMarked();
                pool.DeallocateAllMarked();
            }
        }

        private bool Intersected(Projectile p,  List<Trajectile> drawList, HitEntity hitEntity)
        {
            if (hitEntity?.HitPos == null) return false;
            if (p.EnableAv && (p.DrawLine || p.ModelId != -1))
            {
                var hitPos = hitEntity.HitPos.Value;
                p.TestSphere.Center = hitPos;
                p.T.OnScreen = Session.Instance.Session.Camera.IsInFrustum(ref p.TestSphere);

                if (p.T.MuzzleId != -1)
                {
                    var length = Vector3D.Distance(p.LastPosition, hitPos);
                    p.T.UpdateShape(p.LastPosition, hitPos, p.Direction, length);
                    p.T.Complete(hitEntity, true);
                    drawList.Add(p.T);
                }
            }

            p.Colliding = true;
            if (!p.T.System.VirtualBeams) Hits.Enqueue(p);
            else
            {
                p.T.WeaponCache.VirtualHit = true;
                p.T.WeaponCache.HitEntity.Entity = hitEntity.Entity;
                p.T.WeaponCache.HitEntity.HitPos = hitEntity.HitPos;
                if (hitEntity.Entity is MyCubeGrid) p.T.WeaponCache.HitBlock = hitEntity.Blocks[0];
                Hits.Enqueue(p);
                CreateFakeBeams(p, hitEntity, drawList);
            }
            if (p.EnableAv) p.HitEffects();
            return true;
        }

        private void CreateFakeBeams(Projectile p, HitEntity hitEntity, List<Trajectile> drawList, bool miss = false)
        {
            for (int i = 0; i < p.VrTrajectiles.Count; i++)
            {
                var vt = p.VrTrajectiles[i];
                vt.OnScreen = p.T.OnScreen;
                if (vt.System.ConvergeBeams)
                {
                    LineD beam;
                    if (!miss)
                    {
                        var hitPos = hitEntity?.HitPos ?? Vector3D.Zero;
                        beam = new LineD(vt.PrevPosition, hitPos);
                    }
                    else beam = new LineD(vt.PrevPosition, p.Position);

                    vt.UpdateVrShape(beam.From, beam.To, beam.Direction, beam.Length);
                }
                else
                {
                    var beamEnd = vt.Position + (vt.Direction * p.MaxTrajectory);
                    var line = new LineD(vt.PrevPosition, beamEnd);
                    if (!miss)
                    {
                        var hitBlock = p.T.WeaponCache.HitBlock;
                        Vector3D center;
                        hitBlock.ComputeWorldCenter(out center);

                        Vector3 halfExt;
                        hitBlock.ComputeScaledHalfExtents(out halfExt);

                        var blockBox = new BoundingBoxD(-halfExt, halfExt);
                        var rotMatrix = Quaternion.CreateFromRotationMatrix(hitBlock.CubeGrid.WorldMatrix);
                        var obb = new MyOrientedBoundingBoxD(center, blockBox.HalfExtents, rotMatrix);

                        var dist = obb.Intersects(ref line) ?? Vector3D.Distance(line.From, center);
                        var hitVec = line.From + (line.Direction * dist);
                        vt.UpdateVrShape(line.From, hitVec, line.Direction, dist);
                    }
                    else vt.UpdateVrShape(line.From, line.To, line.Direction, line.Length);
                }
                vt.Complete(hitEntity, true);
                drawList.Add(vt);
                p.T.WeaponCache.Hits++;
            }
        }

        private void Die(Projectile p, int poolId)
        {
            var dInfo = p.T.System.Values.Ammo.AreaEffect.Detonation;
            if (p.MoveToAndActivate || dInfo.DetonateOnEnd && (!dInfo.ArmOnlyOnHit || p.T.ObjectsHit > 0))
            {
                GetEntitiesInBlastRadius(p, poolId);
                var hitEntity = p.T.HitList[0];
                if (hitEntity != null)
                {
                    p.LastHitPos = hitEntity.HitPos;
                    p.LastHitEntVel = hitEntity.Entity?.Physics?.LinearVelocity;
                }
            }
            else p.ProjectileClose(this, poolId);
        }

        //Relative velocity proportional navigation
        //aka: Whip-Nav
        private Vector3D CalculateMissileIntercept(Vector3D targetPosition, Vector3D targetVelocity, Vector3D missilePos, Vector3D missileVelocity, double missileAcceleration, double compensationFactor = 1, double maxLateralThrustProportion = 0.5)
        {
            var missileToTarget = Vector3D.Normalize(targetPosition - missilePos);
            var relativeVelocity = targetVelocity - missileVelocity;
            var parallelVelocity = relativeVelocity.Dot(missileToTarget) * missileToTarget;
            var normalVelocity = (relativeVelocity - parallelVelocity);

            var normalMissileAcceleration = normalVelocity * compensationFactor;

            if (Vector3D.IsZero(normalMissileAcceleration))
                return missileToTarget * missileAcceleration;

            double maxLateralThrust = missileAcceleration * Math.Min(1, Math.Max(0, maxLateralThrustProportion));
            if (normalMissileAcceleration.LengthSquared() > maxLateralThrust * maxLateralThrust)
            {
                Vector3D.Normalize(ref normalMissileAcceleration, out normalMissileAcceleration);
                normalMissileAcceleration *= maxLateralThrust;
            }
            double diff = missileAcceleration * missileAcceleration - normalMissileAcceleration.LengthSquared();
            var maxedDiff = Math.Max(0, diff); 
            return Math.Sqrt(maxedDiff) * missileToTarget + normalMissileAcceleration;
        }
    }
}
