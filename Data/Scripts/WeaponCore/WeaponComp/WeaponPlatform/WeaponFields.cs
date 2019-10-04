﻿using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRageMath;
using WeaponCore.Support;

namespace WeaponCore.Platform
{
    public partial class Weapon
    {
        internal const uint SuspendAmmoCount = 300;
        internal const uint UnSuspendAmmoCount = 1200;
        internal int NextMuzzle;
        internal static bool UiSet;
        internal volatile bool Casting;

        private readonly Vector3 _localTranslation;
        private readonly int _numOfBarrels;
        private HashSet<string> _muzzlesToFire = new HashSet<string>();
        internal readonly Dictionary<int, string> MuzzleIDToName = new Dictionary<int, string>();

        private int _shotsInCycle;
        private int _shots = 1;
        private int _nextVirtual;
        private uint _ticksUntilShoot;
        private uint _posChangedTick = 1;
        private uint _lastShotTick;
        private uint _ReloadedTick;
        internal uint TicksPerShot;
        internal double TimePerShot;

        private bool _newCycle;
        //private bool _firstRun = true;

        internal MyEntity EntityPart;
        internal MyEntity BarrelPart;
        internal WeaponSystem System;
        internal Dummy[] Dummies;
        internal Muzzle[] Muzzles;
        internal uint[] BeamSlot;
        internal WeaponComponent Comp;

        internal WeaponFrameCache WeaponCache = new WeaponFrameCache();

        internal Target Target;
        internal Target NewTarget;
        internal Vector3D TargetPos;
        internal MathFuncs.Cone AimCone = new MathFuncs.Cone();
        internal Matrix BarrelRotationPerShot;
        internal MyParticleEffect[] BarrelEffects1;
        internal MyParticleEffect[] BarrelEffects2;
        internal MyParticleEffect[] HitEffects;
        internal MySoundPair ReloadSound;
        internal MySoundPair FiringSound;
        internal MySoundPair RotateSound;
        internal readonly MyEntity3DSoundEmitter ReloadEmitter;
        internal readonly MyEntity3DSoundEmitter FiringEmitter;
        internal readonly MyEntity3DSoundEmitter RotateEmitter;
        internal readonly CachingDictionary<Muzzle, uint> BarrelAvUpdater = new CachingDictionary<Muzzle, uint>();
        internal readonly Dictionary<EventTriggers, HashSet<PartAnimation>> AnimationsSet;
        internal readonly Dictionary<MyEntity, Vector3D> SleepingTargets = new Dictionary<MyEntity, Vector3D>();
        internal float RequiredPower;
        internal float BaseDamage;
        internal float ShotEnergyCost;
        internal float DPS;
        internal float areaEffectDmg;
        internal float detonateDmg;
        internal float LastHeat;
        internal uint SuspendAmmoTick;
        internal uint UnSuspendAmmoTick;
        internal uint ShotCounter;
        internal uint LastTargetCheck;
        internal uint LastTargetLock;
        internal uint FirstFireDelay;
        internal uint OffDelay;
        internal int TargetAttempts;
        internal int TargetDelayMulti;
        internal int MaxAttempts = 4;
        internal int DelayPerAttempt = 30;
        internal int RateOfFire;
        internal int CurrentAmmo;
        internal int AmmoMagTimer = int.MaxValue;
        internal int DelayFireCount;
        internal int WeaponId;
        internal int HsRate;
        internal int EnergyPriority;
        internal int LastBlockCount;
        internal float HeatPShot;
        internal MyFixedPoint CurrentMags;
        internal double Azimuth;
        internal double Elevation;
        internal double DesiredAzimuth;
        internal double DesiredElevation;
        internal double AimingTolerance;
        internal double RotationSpeed;
        internal double ElevationSpeed;
        internal double MaxAzimuthRadians;
        internal double MinAzimuthRadians;
        internal double MaxElevationRadians;
        internal double MinElevationRadians;
        internal bool IsTurret;
        internal bool TurretMode;
        internal bool TrackTarget;
        internal bool AiReady;
        internal bool SeekTarget;
        internal bool TrackingAi;
        internal bool IsTracking;
        internal bool IsAligned;
        internal bool AmmoSuspend;
        internal bool AmmoFull;
        internal bool IsShooting;
        internal bool BarrelMove;
        internal bool PlayTurretAv;
        internal bool AvCapable;
        internal bool DelayCeaseFire;
        internal bool TargetWasExpired;
        internal bool Reloading;
        internal bool FirstLoad = true;
        internal bool ReturnHome;
        internal bool CurrentlyDegrading;
        internal bool SleepTargets;
        internal bool HitOther;
        internal TerminalActionState ManualShoot = TerminalActionState.ShootOff;
        internal HardPointDefinition.Prediction Prediction;

        internal enum TerminalActionState
        {
            ShootOn,
            ShootOff,
            ShootOnce,
            ShootClick,
        }

        internal bool LoadAmmoMag
        {
            set
            {
                if (value)
                {
                    Comp.BlockInventory.RemoveItemsOfType(1, System.AmmoDefId);
                    AmmoMagTimer = FirstLoad ? 1 : System.ReloadTime;
                    _ReloadedTick = Session.Instance.Tick + (uint)AmmoMagTimer;
                    FirstLoad = false;
                }
            }
        }

        internal bool AmmoMagLoaded
        {
            get
            {
                if (_ReloadedTick > Session.Instance.Tick) return false;
                CurrentAmmo = System.MagazineDef.Capacity;
                AmmoMagTimer = int.MaxValue;
                return true;
            }
        }

        public enum EventTriggers
        {
            Reloading,
            Firing,
            Tracking,
            Overheated,
            TurnOn,
            TurnOff,
            BurstReload,
            OutOfAmmo,
            PreFire,
            EmptyOnGameLoad
        }

        public Weapon(MyEntity entity, WeaponSystem system, int weaponId, WeaponComponent comp, Dictionary<EventTriggers, HashSet<PartAnimation>> animationSets)
        {
            EntityPart = entity;
            AnimationsSet = animationSets;
            _localTranslation = entity.PositionComp.LocalMatrix.Translation;
            System = system;
            Comp = comp;
            comp.HasEnergyWeapon = comp.HasEnergyWeapon || System.EnergyAmmo || System.IsHybrid;

            AvCapable = System.HasBarrelShootAv && !Session.Instance.DedicatedServer;

            if (AvCapable && system.FiringSound == WeaponSystem.FiringSoundState.WhenDone)
            {
                FiringEmitter = new MyEntity3DSoundEmitter(Comp.MyCube, true, 1f);
                FiringSound = new MySoundPair();
                FiringSound.Init(System.Values.Audio.HardPoint.FiringSound);
            }

            if (AvCapable && system.WeaponReloadSound)
            {
                ReloadEmitter = new MyEntity3DSoundEmitter(Comp.MyCube, true, 1f);
                ReloadSound = new MySoundPair();
                ReloadSound.Init(System.Values.Audio.HardPoint.ReloadSound);
            }

            if (AvCapable && system.BarrelRotationSound && system.Values.HardPoint.RotateBarrelAxis != 0)
            {
                RotateEmitter = new MyEntity3DSoundEmitter(Comp.MyCube, true, 1f);
                RotateSound = new MySoundPair();
                RotateSound.Init(System.Values.Audio.HardPoint.BarrelRotationSound);
            }

            if (AvCapable)
            {
                if (System.BarrelEffect1) BarrelEffects1 = new MyParticleEffect[System.Values.Assignments.Barrels.Length];
                if (System.BarrelEffect2) BarrelEffects2 = new MyParticleEffect[System.Values.Assignments.Barrels.Length];
                if (System.HitParticle && System.IsBeamWeapon) HitEffects = new MyParticleEffect[System.Values.Assignments.Barrels.Length];
            }

            WeaponId = weaponId;
            IsTurret = System.Values.HardPoint.Block.TurretAttached;
            TurretMode = System.Values.HardPoint.Block.TurretController;
            TrackTarget = System.Values.HardPoint.Block.TrackTargets;
            HsRate = System.Values.HardPoint.Loading.HeatSinkRate;
            EnergyPriority = System.Values.HardPoint.EnergyPriority;
            var toleranceInRadians = MathHelper.ToRadians(System.Values.HardPoint.AimingTolerance);
            AimCone.ConeAngle = toleranceInRadians;
            AimingTolerance = Math.Cos(toleranceInRadians);
            Prediction = System.Values.HardPoint.AimLeadingPrediction;

            _numOfBarrels = System.Barrels.Length;
            DelayCeaseFire = System.TimeToCeaseFire > 0;
            BeamSlot = new uint[_numOfBarrels];
            Target = new Target(comp.MyCube);
            NewTarget = new Target(comp.MyCube);
            if (System.MaxTrajectorySqr > Comp.Ai.MaxTargetingRangeSqr)
            {
                Comp.Ai.MaxTargetingRange = System.MaxTrajectory;
                Comp.Ai.MaxTargetingRangeSqr = System.MaxTrajectorySqr;
            }
            Comp.UpdatePivotPos(this);
        }

        internal void UpdateRequiredPower()
        {
            if (System.EnergyAmmo || System.IsHybrid)
                RequiredPower = ((ShotEnergyCost * (RateOfFire * MyEngineConstants.PHYSICS_STEP_SIZE_IN_SECONDS)) * System.Values.HardPoint.Loading.BarrelsPerShot) * System.Values.HardPoint.Loading.TrajectilesPerBarrel;
            else
                RequiredPower = Comp.IdlePower;

            Comp.MaxRequiredPower += RequiredPower;
        }

        internal void UpdateShotEnergy()
        {
            var ewar = (int)System.Values.Ammo.AreaEffect.AreaEffect > 3;
            ShotEnergyCost = ewar ? System.Values.HardPoint.EnergyCost * areaEffectDmg : System.Values.HardPoint.EnergyCost * BaseDamage;
        }

        internal void UpdateBarrelRotation()
        {
            if (!Comp.MyCube.MarkedForClose && Comp.MyCube != null)
            {
                var rof = RateOfFire < 3599 ? RateOfFire : 3599;

                var angle = MathHelper.ToRadians((360f / System.Barrels.Length) / (3600f / rof));


                var axis = System.Values.HardPoint.RotateBarrelAxis;
                if (axis != 0 && BarrelPart != Comp.MyCube)
                {
                    var partPos = (Vector3) Session.Instance.GetPartLocation("subpart_" + System.MuzzlePartName.String,
                        ((MyEntitySubpart) BarrelPart).Parent.Model);

                    var to = Matrix.CreateTranslation(-partPos);
                    var from = Matrix.CreateTranslation(partPos);

                    Matrix rotationMatrix = Matrix.Zero;
                    switch (axis)
                    {
                        case 1:
                            rotationMatrix = to * Matrix.CreateRotationX(angle) * from;
                            break;
                        case 2:
                            rotationMatrix = to * Matrix.CreateRotationY(angle) * from;
                            break;
                        case 3:
                            rotationMatrix = to * Matrix.CreateRotationZ(angle) * from;
                            break;
                    }

                    BarrelRotationPerShot = rotationMatrix;
                }
            }
        }
    }
}
