using System;
using HarmonyLib;

namespace ZedTank;

// While you drive the tank the mouse aims the turret, so your own gun must not fire.
[HarmonyPatch(typeof(HumanCharacterController), "OnMouseDown_Leven")]
internal static class P_MouseDown
{
	private static bool Prefix(HumanCharacterController __instance)
	{
		return !(Tank.Crewing && __instance.isPlayer);
	}
}

[HarmonyPatch(typeof(HumanCharacterController), "OnMouseHold_Leven")]
internal static class P_MouseHold
{
	private static bool Prefix(HumanCharacterController __instance)
	{
		return !(Tank.Crewing && __instance.isPlayer);
	}
}

// The tank's name in prompts and the vehicle UI.
[HarmonyPatch(typeof(TextManager), "GetText", new Type[] { typeof(int) })]
internal static class P_Name1
{
	private static void Postfix(int textid, ref string __result)
	{
		if (textid == Tank.NameId) __result = "Tank";
		else if (textid == Tank.NameId3D) __result = "Battle Tank";
	}
}

[HarmonyPatch(typeof(TextManager), "GetText", new Type[] { typeof(int), typeof(GameLanguage) })]
internal static class P_Name2
{
	private static void Postfix(int textid, ref string __result)
	{
		if (textid == Tank.NameId) __result = "Tank";
		else if (textid == Tank.NameId3D) __result = "Battle Tank";
	}
}

[HarmonyPatch(typeof(TextManager), "GetText", new Type[] { typeof(int), typeof(string) })]
internal static class P_Name3
{
	private static void Postfix(int textid, ref string __result)
	{
		if (textid == Tank.NameId) __result = "Tank";
		else if (textid == Tank.NameId3D) __result = "Battle Tank";
	}
}

// The tank's storage window is titled with the base car's name ("Pickup"): use the tank's name.
[HarmonyPatch(typeof(LootInventoryPanel), "OpenPanelCore")]
internal static class P_LootTitle
{
	private static void Prefix(InventoryData m_inventoryData, ref string m_name)
	{
		try
		{
			if (m_inventoryData == null) return;
			var live = BasicVehicle.s_liveVehicles;
			if (live == null) return;
			for (int i = 0; i < live.Count; i++)
			{
				BasicVehicle v = live[i];
				if (!Tank.IsTank(v)) continue;
				InventoryData inv = v.vehicleData?.inventoryData;
				if (inv != null && inv.Pointer == m_inventoryData.Pointer)
				{
					m_name = Tank.Is3D(v) ? "Battle Tank" : "Tank";
					return;
				}
			}
		}
		catch { }
	}
}
