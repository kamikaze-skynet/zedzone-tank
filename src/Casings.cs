using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ZedTank;

// Spent casings come back: every main gun shot puts a 105 mm Shell Casing in your bag, every
// machine gun round its brass (.50 BMG / 5.56 / 7.62x39 Shell, the game's own items). New shells
// are crafted from a casing at the reloader, and the game's cartridges get a reloader recipe that
// uses their casings if the game hasn't one already.
internal static class Casings
{
	internal const string CasingGuid = "105ca5e0-7a4c-4b2d-9e10-105a7a4c0108";
	internal const int RequestedCasingId = 881108;

	// machine gun round -> its casing (game items)
	internal static readonly Dictionary<int, int> Brass = new() { { 106, 1006 }, { 102, 1002 }, { 104, 1004 } };

	// reloader recipe per cartridge when the game has none that uses the casing:
	// 10 casings + Gunpowder + Copper Plate -> 10 rounds
	private static readonly Dictionary<int, (int powder, int copper)> Reload = new() { { 106, (3, 1) }, { 102, (1, 1) }, { 104, (1, 1) } };
	private const int Gunpowder = 7, CopperPlate = 9;

	private static bool _done;
	private static float _retry;

	internal static int CasingId() => Bundled.CasingId();

	// once the items are loaded: point the shell recipe at the casing's real id, add reload recipes
	internal static void Check()
	{
		if (_done) return;
		float now = Time.realtimeSinceStartup;
		if (now < _retry) return;
		_retry = now + 5f;
		ItemManager im = ItemManager.instance;
		if (im == null || im.itemList == null || im.itemList.Count == 0) return;
		int casing = CasingId(), shell = Bundled.ShellId();
		if (casing <= 0 || shell <= 0) return;
		_done = true;
		var sb = new StringBuilder($"Casings: 105 mm casing {casing}, shell {shell}");
		try
		{
			ItemAttr s = im.FindItemAttrById(shell);
			if (s != null && casing != RequestedCasingId) Remap(s, RequestedCasingId, casing, sb);
		}
		catch (Exception e) { sb.Append(" | shell recipe: " + e.Message); }
		if (TankPlugin.ReloadRecipes.Value)
			foreach (var kv in Reload) AddReload(im, kv.Key, Brass[kv.Key], kv.Value.powder, kv.Value.copper, sb);
		TLog.Info(sb.ToString());
	}

	private static void Remap(ItemAttr a, int from, int to, StringBuilder sb)
	{
		var recipes = a.recipeDataAry;
		if (recipes == null) return;
		for (int r = 0; r < recipes.Length; r++)
		{
			var items = recipes[r]?.recipeItems;
			if (items == null) continue;
			for (int i = 0; i < items.Count; i++)
			{
				var it = items[i];
				if (it == null || it.itemId != from) continue;
				it.itemId = to;
				sb.Append($" | shell recipe casing {from}->{to}");
			}
		}
		try
		{
			var reg = ItemManager.instance.GetRecipeDatasById(a.itemId);
			if (reg != null)
				foreach (var rd in reg)
				{
					var items = rd?.recipeItems;
					if (items == null) continue;
					for (int i = 0; i < items.Count; i++) if (items[i] != null && items[i].itemId == from) items[i].itemId = to;
				}
		}
		catch { }
	}

	private static void AddReload(ItemManager im, int round, int brass, int powder, int copper, StringBuilder sb)
	{
		try
		{
			ItemAttr a = im.FindItemAttrById(round);
			if (a == null || im.FindItemAttrById(brass) == null) { sb.Append($" | {round}: item missing"); return; }
			var have = im.GetRecipeDatasById(round);
			int n = have != null ? have.Length : 0;
			if (have != null)
				foreach (var rd in have)
				{
					var items = rd?.recipeItems;
					if (items == null) continue;
					for (int i = 0; i < items.Count; i++)
						if (items[i] != null && items[i].itemId == brass)
						{
							sb.Append($" | {Name(a)} already reloads from its casings ({rd.craftPlatform})");
							return;
						}
				}
			var r = new RecipeData();
			r.itemId = round;
			r.outputItemNumber = 10;
			r.craftPlatform = CraftPlatform.reloader;
			var list = new Il2CppSystem.Collections.Generic.List<RecipeItemData>();
			foreach (var (id, num) in new[] { (brass, 10), (Gunpowder, powder), (CopperPlate, copper) })
			{
				var ri = new RecipeItemData(); ri.itemId = id; ri.itemNumber = num; list.Add(ri);
			}
			r.recipeItems = list;
			var old = a.recipeDataAry;
			var only = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<RecipeData>(1);
			only[0] = r;
			a.recipeDataAry = only;
			im.RegisterModRecipes(a);
			// keep the game's own recipes on the item too
			int ol = old != null ? old.Length : 0;
			var all = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<RecipeData>(ol + 1);
			for (int i = 0; i < ol; i++) all[i] = old[i];
			all[ol] = r;
			a.recipeDataAry = all;
			var check = im.GetRecipeDatasById(round);
			sb.Append($" | {Name(a)}: reloader recipe added ({n} -> {(check != null ? check.Length : 0)} recipes)");
		}
		catch (Exception e) { sb.Append($" | {round}: " + e.Message); }
	}

	private static string Name(ItemAttr a)
	{
		try { return a.ItemName_EN ?? a.itemId.ToString(); } catch { return a.itemId.ToString(); }
	}

	// One casing back to the player: onto a stack they carry, else onto one in the tank's cargo,
	// else as a new item (the game drops it at their feet if the bag is full).
	internal static void Give(HumanCharacterController pc, BasicVehicle v, int id)
	{
		if (!TankPlugin.ReturnCasings.Value || id <= 0) return;
		try
		{
			ItemManager im = ItemManager.instance;
			ItemAttr a = im?.FindItemAttrById(id);
			if (a == null) return;
			float max = Mathf.Max(1, a.stackNumber);
			var boxes = Tank.Carried(pc);
			try { if (v?.vehicleData?.inventoryData != null) boxes.Add(v.vehicleData.inventoryData); } catch { }
			foreach (InventoryData box in boxes)
			{
				var l = box.itemList;
				if (l == null) continue;
				for (int i = 0; i < l.Count; i++)
				{
					ItemData it = l[i];
					if (it == null || it.itemId != id) continue;
					float have = Mathf.Max(1f, it.itemNumberFloat);
					if (have + 1f > max + 0.001f) continue;
					it.itemNumberFloat = have + 1f;
					try { box.RefreshTotalItemWeight(); } catch { }
					return;
				}
			}
			ItemData d = im.GetRandomDataByAttr(a, false, false);
			if (d == null) return;
			d.itemNumberFloat = 1f;
			d.durability = InventoryData.MAX_ITEM_DURABILITY;
			GameController.instance.AddItemToPlayer(d, true);
		}
		catch (Exception e) { if (!_warned) { _warned = true; TLog.Warn("casings: " + e.Message); } }
	}

	private static bool _warned;
}
