using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace ZedTank;

// A "VEHICLES" tab in the game's Build menu with the Battle Tank in it. The tank is a made-up
// terrain object entry (never placed): pressing BUILD takes the materials you carry and rolls a
// Battle Tank out a few tiles in front of you, like the Battle Tank Kit does.
internal static class BuildMenu
{
	internal const int AttrId = 881300;
	internal const TechGenre Genre = (TechGenre)6;
	internal const string TabName = "VEHICLES";
	internal const string Name = "Battle Tank";
	internal const string Desc = "A main battle tank with a 105 mm gun and a roof machine gun. Built from the materials you carry: it rolls out a few tiles in front of you.";
	// same as the Battle Tank Kit: Iron Plate, Metal Component, Copper Tube, Iron Wire, Copper Wire
	internal static readonly (int id, int n)[] Recipe = { (8, 60), (62, 20), (11, 10), (12, 20), (13, 10) };

	private static TerrainObjectAttr _attr;
	private static Sprite _icon;
	private static bool _logged;

	internal static bool On => TankPlugin.BuildMenuTab.Value;

	internal static TerrainObjectAttr Attr()
	{
		if (_attr != null) return _attr;
		try
		{
			var a = ScriptableObject.CreateInstance<TerrainObjectAttr>();
			((UnityEngine.Object)a).name = "zt_battle_tank";
			a.hideFlags = HideFlags.DontUnloadUnusedAsset;
			a.id = AttrId;
			a.englishName = Name;
			a.chineseName = Name;
			a.chineseDescription = Desc;
			a.techGenre = Genre;
			a.unlockByDefault = true;
			a.defaultHP = 6000f;
			a.sortingPriority = 0;
			var r = new RecipeData();
			r.itemId = AttrId;
			r.outputItemNumber = 1;
			r.craftPlatform = CraftPlatform.byhand;
			var items = new Il2CppSystem.Collections.Generic.List<RecipeItemData>();
			foreach (var (id, n) in Recipe) { var ri = new RecipeItemData(); ri.itemId = id; ri.itemNumber = n; items.Add(ri); }
			r.recipeItems = items;
			a.recipeData = r;
			_attr = a;
		}
		catch (Exception e) { TLog.Warn("Build menu: making the Battle Tank entry failed: " + e.Message); }
		return _attr;
	}

	internal static Sprite Icon()
	{
		if (_icon != null) return _icon;
		try
		{
			using var st = typeof(BuildMenu).Assembly.GetManifestResourceStream("art.battletank_icon.png");
			if (st == null) return null;
			var b = new byte[st.Length];
			int got = 0;
			while (got < b.Length) { int r = st.Read(b, got, b.Length - got); if (r <= 0) break; got += r; }
			var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
			tex.hideFlags = HideFlags.HideAndDontSave;
			ImageConversion.LoadImage(tex, (Il2CppStructArray<byte>)b);
			_icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
			_icon.hideFlags = HideFlags.HideAndDontSave;
		}
		catch (Exception e) { TLog.Warn("Build menu icon: " + e.Message); }
		return _icon;
	}

	// Our entry in the game's lookups, and our genre in the menu's tab order.
	internal static void Register()
	{
		TerrainObjectAttr a = Attr();
		if (a == null) return;
		try
		{
			GameController gc = GameController.instance;
			if (gc != null)
			{
				var dic = gc.terrainObjectAttrDic;
				// borrow a real object's sprite name so code that looks up the entry's art finds some
				// (our card and details show the tank icon instead)
				if (dic != null && string.IsNullOrEmpty(a.spriteName))
					foreach (var kv in dic)
					{
						TerrainObjectAttr o = kv.Value;
						if (o == null || kv.Key == AttrId || string.IsNullOrEmpty(o.spriteName) || o.techGenre != TechGenre.Production) continue;
						a.spriteName = o.spriteName; a.spriteCount = o.spriteCount;
						TLog.Info($"Build menu: placeholder art from '{o.englishName}' ({o.spriteName})");
						break;
					}
				if (dic != null && !dic.ContainsKey(AttrId)) dic.Add(AttrId, a);
				var gd = gc.terrainObjectAttrTechGenreDic;
				if (gd != null)
				{
					if (!gd.ContainsKey(Genre)) gd.Add(Genre, new Il2CppSystem.Collections.Generic.List<TerrainObjectAttr>());
					var l = gd[Genre];
					if (!l.Contains(a)) l.Add(a);
				}
			}
			var order = ConstructionPanel.GENRE_ORDER;
			bool has = false;
			if (order != null) foreach (TechGenre g in order) if (g == Genre) has = true;
			if (!has)
			{
				int n = order != null ? order.Length : 0;
				var arr = new Il2CppStructArray<TechGenre>(n + 1);
				for (int i = 0; i < n; i++) arr[i] = order[i];
				arr[n] = Genre;
				ConstructionPanel.GENRE_ORDER = arr;
			}
			if (!_logged) { _logged = true; TLog.Info($"Build menu: '{TabName}' tab with the {Name} added (tabs {(order != null ? order.Length : 0)} -> {ConstructionPanel.GENRE_ORDER.Length})"); }
		}
		catch (Exception e) { if (!_logged) { _logged = true; TLog.Warn("Build menu: " + e.Message); } }
	}

	// The menu fills in card and detail icons after our patches run, so keep ours on screen while it's open.
	internal static void Tick()
	{
		if (!On || _icon == null && Icon() == null) return;
		try
		{
			ConstructionPanel p = ConstructionPanel._instance;
			if (p == null || !p.built || p.windowRT == null || !p.windowRT.gameObject.activeInHierarchy) return;
			var cards = p.cards;
			if (cards != null)
				for (int i = 0; i < cards.Count; i++)
				{
					var c = cards[i];
					if (c == null || c.isVoxelCard || c.terrainObjectId != AttrId || c.iconImage == null) continue;
					if (c.iconImage.sprite == null || c.iconImage.sprite.Pointer != _icon.Pointer) { c.iconImage.sprite = _icon; c.iconImage.preserveAspect = true; }
				}
			if (p.selectedId == AttrId && !p.voxelTabSelected && p.detailIcon != null && (p.detailIcon.sprite == null || p.detailIcon.sprite.Pointer != _icon.Pointer))
			{ p.detailIcon.sprite = _icon; p.detailIcon.preserveAspect = true; }
		}
		catch (Exception e) { if (!_tickWarned) { _tickWarned = true; TLog.Warn("Build menu icon: " + e.Message); } }
	}
	private static bool _tickWarned;

	internal static bool Afford(HumanCharacterController pc, out string missing)
	{
		missing = null;
		var boxes = Tank.Carried(pc);
		foreach (var (id, n) in Recipe)
			if (Tank.Count(boxes, id) < n)
			{
				string nm = null;
				try { nm = TextManager.instance?.GetText(1000000 + id); } catch { }
				missing = $"{nm ?? ("item " + id)} {Tank.Count(boxes, id)}/{n}";
				return false;
			}
		return true;
	}

	internal static void Build(ConstructionPanel panel)
	{
		HumanCharacterController pc = GameController.instance?.playerCharacter;
		if (pc == null) return;
		if (!Afford(pc, out string missing)) { Tank.Fail("Not enough materials for the Battle Tank: " + missing); return; }
		string r = Tank.CallIn(pc, true, false, true);
		if (r == null || r.StartsWith("!")) return;
		var boxes = Tank.Carried(pc);
		foreach (var (id, n) in Recipe)
		{
			float took = AutoIO.Take(boxes, id, n);
			if (took < n) TLog.Warn($"Build menu: only took {took}/{n} of item {id}");
		}
		try { panel.ClosePanel(); } catch { }
	}
}

[HarmonyPatch(typeof(ConstructionPanel), "EnsureBuilt")]
internal static class P_BuildEnsure
{
	private static void Prefix() { if (BuildMenu.On) BuildMenu.Register(); }
}

[HarmonyPatch(typeof(ConstructionPanel), "OpenPanel")]
internal static class P_BuildOpen
{
	private static void Prefix() { if (BuildMenu.On) BuildMenu.Register(); }
}

[HarmonyPatch(typeof(ConstructionPanel), "BuildGenreTab")]
internal static class P_BuildTab
{
	private static void Postfix(ConstructionPanel __instance, TechGenre genre)
	{
		if (genre != BuildMenu.Genre) return;
		try
		{
			var tabs = __instance.tabs;
			if (tabs == null) return;
			for (int i = 0; i < tabs.Count; i++)
				if (tabs[i] != null && tabs[i].genre == BuildMenu.Genre && tabs[i].nameText != null) tabs[i].nameText.text = BuildMenu.TabName;
		}
		catch (Exception e) { TLog.Warn("Build menu tab: " + e.Message); }
	}
}

[HarmonyPatch(typeof(GameController), "GetAvailableTerrainObjectAttrsByTechGenre")]
internal static class P_BuildList
{
	private static void Postfix(TechGenre m_techGenre, ref Il2CppSystem.Collections.Generic.List<TerrainObjectAttr> __result)
	{
		if (m_techGenre != BuildMenu.Genre || !BuildMenu.On) return;
		TerrainObjectAttr a = BuildMenu.Attr();
		if (a == null) return;
		if (__result == null) __result = new Il2CppSystem.Collections.Generic.List<TerrainObjectAttr>();
		if (!__result.Contains(a)) __result.Add(a);
	}
}

[HarmonyPatch(typeof(GameController), "GetTerrainObjectAttrById")]
internal static class P_BuildAttr
{
	private static void Postfix(int id, ref TerrainObjectAttr __result)
	{
		if (id == BuildMenu.AttrId && __result == null) __result = BuildMenu.Attr();
	}
}

[HarmonyPatch(typeof(ConstructionItemCardUI), "Build")]
internal static class P_BuildCard
{
	private static void Prefix(TerrainObjectAttr attr, ref Sprite iconSprite)
	{
		if (attr != null && attr.id == BuildMenu.AttrId) iconSprite = BuildMenu.Icon() ?? iconSprite;
	}

	private static void Postfix(ConstructionItemCardUI __instance, TerrainObjectAttr attr)
	{
		if (attr == null || attr.id != BuildMenu.AttrId) return;
		try
		{
			if (__instance.nameText != null) __instance.nameText.text = BuildMenu.Name;
			if (__instance.iconImage != null && BuildMenu.Icon() != null) { __instance.iconImage.sprite = BuildMenu.Icon(); __instance.iconImage.preserveAspect = true; }
		}
		catch { }
	}
}

[HarmonyPatch(typeof(ConstructionPanel), "ShowTerrainObjectDetails")]
internal static class P_BuildDetails
{
	private static void Postfix(ConstructionPanel __instance, int m_terrainObjectId)
	{
		if (m_terrainObjectId != BuildMenu.AttrId) return;
		try
		{
			if (__instance.detailIcon != null && BuildMenu.Icon() != null) { __instance.detailIcon.sprite = BuildMenu.Icon(); __instance.detailIcon.preserveAspect = true; }
			if (__instance.detailNameText != null) __instance.detailNameText.text = BuildMenu.Name;
			if (__instance.detailGenreText != null) __instance.detailGenreText.text = BuildMenu.TabName;
			if (__instance.detailDescText != null) __instance.detailDescText.text = BuildMenu.Desc;
		}
		catch (Exception e) { TLog.Warn("Build menu details: " + e.Message); }
	}
}

[HarmonyPatch(typeof(ConstructionPanel), "BuildSelected")]
internal static class P_BuildGo
{
	private static bool Prefix(ConstructionPanel __instance)
	{
		try
		{
			if (__instance.selectedId != BuildMenu.AttrId || __instance.voxelTabSelected) return true;
			BuildMenu.Build(__instance);
			return false;
		}
		catch (Exception e) { TLog.Warn("Build menu build: " + e.Message); return false; }
	}
}

// a double-click on a card builds it at once: do the same for the tank
[HarmonyPatch(typeof(ConstructionPanel), "OnCardClicked")]
internal static class P_BuildCardClick
{
	private static bool Prefix(ConstructionPanel __instance, ConstructionItemCardUI card, int clickCount)
	{
		try
		{
			if (card == null || card.isVoxelCard || card.terrainObjectId != BuildMenu.AttrId || clickCount < 2) return true;
			__instance.SelectItem(BuildMenu.AttrId, true);
			BuildMenu.Build(__instance);
			return false;
		}
		catch { return true; }
	}
}

// the entry's name, description and "can be built" come from getters keyed off game data
[HarmonyPatch(typeof(TerrainObjectAttr), "name", MethodType.Getter)]
internal static class P_BuildAttrName
{
	private static void Postfix(TerrainObjectAttr __instance, ref string __result)
	{
		try { if (__instance.id == BuildMenu.AttrId) __result = BuildMenu.Name; } catch { }
	}
}

[HarmonyPatch(typeof(TerrainObjectAttr), "description", MethodType.Getter)]
internal static class P_BuildAttrDesc
{
	private static void Postfix(TerrainObjectAttr __instance, ref string __result)
	{
		try { if (__instance.id == BuildMenu.AttrId) __result = BuildMenu.Desc; } catch { }
	}
}

[HarmonyPatch(typeof(TerrainObjectAttr), "canBeBuilt", MethodType.Getter)]
internal static class P_BuildAttrCan
{
	private static void Postfix(TerrainObjectAttr __instance, ref bool __result)
	{
		try { if (__instance.id == BuildMenu.AttrId) __result = true; } catch { }
	}
}
