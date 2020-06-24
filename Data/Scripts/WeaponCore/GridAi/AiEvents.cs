﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using static WeaponCore.Session;

namespace WeaponCore.Support
{
    public partial class GridAi
    {
        internal void RegisterMyGridEvents(bool register = true, MyCubeGrid grid = null)
        {
            if (grid == null) grid = MyGrid;

            if (register) {

                if (Registered)
                    Log.Line($"Ai RegisterMyGridEvents error");

                Registered = true;
                MarkedForClose = false;
                grid.OnFatBlockAdded += FatBlockAdded;
                grid.OnFatBlockRemoved += FatBlockRemoved;
                grid.OnClose += GridClose;
            }
            else {

                if (!Registered)
                    Log.Line($"Ai UnRegisterMyGridEvents error");
                MarkedForClose = true;
                if (Registered) {


                    Registered = false;
                    grid.OnFatBlockAdded -= FatBlockAdded;
                    grid.OnFatBlockRemoved -= FatBlockRemoved;
                    grid.OnClose -= GridClose;
                }
            }
        }

        internal void FatBlockAdded(MyCubeBlock myCubeBlock)
        {
            try
            {
                var battery = myCubeBlock as MyBatteryBlock;
                var isWeaponBase = myCubeBlock?.BlockDefinition != null && (Session.ReplaceVanilla && Session.VanillaIds.ContainsKey(myCubeBlock.BlockDefinition.Id) || !string.IsNullOrEmpty(myCubeBlock.BlockDefinition.Id.SubtypeName) && Session.WeaponPlatforms.ContainsKey(myCubeBlock.BlockDefinition.Id.SubtypeId));

                if (isWeaponBase) ScanBlockGroups = true;
                else if (myCubeBlock is MyConveyor || myCubeBlock is IMyConveyorTube || myCubeBlock is MyConveyorSorter || myCubeBlock is MyCargoContainer || myCubeBlock is MyCockpit || myCubeBlock is IMyAssembler)
                {
                    MyInventory inventory;
                    if (myCubeBlock.HasInventory && myCubeBlock.TryGetInventory(out inventory) && Session.UniqueListAdd(inventory, InventoryIndexer, Inventories))
                    {
                        inventory.InventoryContentChanged += CheckAmmoInventory;
                        Session.InventoryItems.TryAdd(inventory, new List<MyPhysicalInventoryItem>());
                        Session.AmmoThreadItemList[inventory] = new List<BetterInventoryItem>();
                    }

                    foreach (var weapon in OutOfAmmoWeapons)
                        Session.CheckStorage.Add(weapon);
                }
                else if (battery != null) {
                    if (Batteries.Add(battery)) SourceCount++;
                    UpdatePowerSources = true;
                }

            }
            catch (Exception ex) { Log.Line($"Exception in Controller FatBlockAdded: {ex} - {myCubeBlock?.BlockDefinition == null}"); }
        }

        private void FatBlockRemoved(MyCubeBlock myCubeBlock)
        {
            try
            {
                MyInventory inventory;
                var battery = myCubeBlock as MyBatteryBlock;
                var isWeaponBase = myCubeBlock?.BlockDefinition != null && (Session.ReplaceVanilla && Session.VanillaIds.ContainsKey(myCubeBlock.BlockDefinition.Id) || !string.IsNullOrEmpty(myCubeBlock.BlockDefinition.Id.SubtypeName) && Session.WeaponPlatforms.ContainsKey(myCubeBlock.BlockDefinition.Id.SubtypeId));

                if (isWeaponBase)
                    ScanBlockGroups = true;
                else if (myCubeBlock.TryGetInventory(out inventory) && Session.UniqueListRemove(inventory, InventoryIndexer, Inventories)) {

                    try {

                        inventory.InventoryContentChanged -= CheckAmmoInventory;
                        List<MyPhysicalInventoryItem> removedPhysical;
                        List<BetterInventoryItem> removedBetter;
                        if (Session.InventoryItems.TryRemove(inventory, out removedPhysical))
                            removedPhysical.Clear();

                        if (Session.AmmoThreadItemList.TryRemove(inventory, out removedBetter))
                            removedBetter.Clear();
                        
                    }
                    catch (Exception ex) { Log.Line($"Exception in FatBlockRemoved inventory: {ex}"); }
                }
                else if (battery != null) {
                    if (Batteries.Remove(battery)) SourceCount--;
                    UpdatePowerSources = true;
                }
            }
            catch (Exception ex) { Log.Line($"Exception in FatBlockRemoved: {ex}"); }
        }

        internal void CheckAmmoInventory(MyInventoryBase inventory, MyPhysicalInventoryItem item, MyFixedPoint amount)
        {
            try
            {
                if (amount <= 0 || item.Content == null || inventory == null) return;
                var itemDef = item.Content.GetObjectId();
                if (Session.AmmoDefIds.Contains(itemDef))
                    Session.FutureEvents.Schedule(CheckReload, itemDef, 1);
            }
            catch (Exception ex) { Log.Line($"Exception in CheckAmmoInventory: {ex}"); }
        }

        internal void GridClose(MyEntity myEntity)
        {
            if (Session == null || MyGrid == null || Closed)
            {
                Log.Line($"[GridClose] Session: {Session != null} - MyGrid:{MyGrid != null} - Closed:{Closed} - myEntity:{myEntity != null}");
                return;
            }

            ProjectileTicker = Session.Tick;
            Session.DelayedGridAiClean.Add(this);
            Session.DelayedGridAiClean.ApplyAdditions();

            if (Session.IsClient)
                Session.SendUpdateRequest(MyGrid.EntityId, PacketType.ClientEntityClosed);
        }
    }
}
