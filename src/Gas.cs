using System;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ZedTank;

// Full gasoline cans for testing (Spawn Menu's Tank kit). Same code as the Helicopter mod.
internal static class Gas
{
	// For Spawn Menu's kit: full cans of gasoline. Picks the container the game itself
	// spawns with gasoline in it (largest one), then makes sure it is full.
	internal static string GiveCans(int count)
	{
		GameController gc = GameController.instance;
		ItemManager im = ItemManager.instance;
		int fuelId = -1;
		try { fuelId = VehicleFuelTransfer.fuelLiquidItemId; } catch { }
		if (gc == null || im == null || fuelId <= 0)
		{
			return "!No fuel: not in a game";
		}
		ItemAttr_Container best = null;
		try
		{
			var e = im.itemAttrDic.GetEnumerator();
			while (e.MoveNext())
			{
				ItemAttr a = e.Current.Value;
				if (a == null)
				{
					continue;
				}
				ItemAttr_Container c = a.TryCast<ItemAttr_Container>();
				if (c == null || c.defaultLiquidIdAry == null)
				{
					continue;
				}
				bool gas = false;
				for (int i = 0; i < c.defaultLiquidIdAry.Length; i++)
				{
					if (c.defaultLiquidIdAry[i] == fuelId)
					{
						gas = true;
						break;
					}
				}
				if (gas && (best == null || c.totalCapacity > best.totalCapacity))
				{
					best = c;
				}
			}
		}
		catch (Exception ex)
		{
			TLog.Info("Tank: fuel can search: " + ex.Message);
		}
		if (best == null)
		{
			return "!No fuel: couldn't find a gasoline can item";
		}
		ItemAttr liquidAttr = null;
		try
		{
			liquidAttr = im.FindItemAttrById(fuelId);
		}
		catch
		{
		}
		int given = 0;
		string canName = "?";
		try
		{
			canName = best.ItemName_EN;
		}
		catch
		{
		}
		for (int n = 0; n < count; n++)
		{
			try
			{
				ItemData can = im.GetRandomDataByAttr(best, false, false);
				if (can == null)
				{
					break;
				}
				can.durability = InventoryData.MAX_ITEM_DURABILITY;
				Fill(can, best, liquidAttr, fuelId, im);
				gc.AddItemToPlayer(can, true);
				given++;
			}
			catch (Exception ex2)
			{
				TLog.Info("Tank: fuel can: " + ex2.Message);
				break;
			}
		}
		TLog.Info($"Tank: gave {given} x {canName} (item {best.itemId}, capacity {best.totalCapacity}) of liquid {fuelId}");
		return given > 0 ? $"{given} full {canName}" : "!No fuel: couldn't make a fuel can";
	}

	private static void Fill(ItemData can, ItemAttr_Container attr, ItemAttr liquidAttr, int fuelId, ItemManager im)
	{
		InventoryData inv = can.inventoryData;
		if (inv == null)
		{
			TLog.Info("Tank: fuel can has no inventory");
			return;
		}
		float cap = attr.totalCapacity;
		var items = inv.itemList;
		ItemData gas = null;
		for (int i = items.Count - 1; i >= 0; i--)
		{
			ItemData it = items[i];
			if (it == null)
			{
				continue;
			}
			if (it.itemId == fuelId && gas == null)
			{
				gas = it;
			}
			else
			{
				try
				{
					inv.RemoveItem(it, true);
				}
				catch
				{
				}
			}
		}
		if (gas == null && liquidAttr != null)
		{
			gas = im.GetRandomDataByAttr(liquidAttr, false, false);
			if (gas != null)
			{
				gas.itemNumberFloat = cap;
				inv.AddItem(gas, true);
			}
		}
		else if (gas != null)
		{
			gas.itemNumberFloat = cap;
		}
		try
		{
			inv.RefreshTotalItemWeight();
		}
		catch
		{
		}
	}
}
