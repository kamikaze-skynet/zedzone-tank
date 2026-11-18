using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ZedTank;

// The tank's own shells must not hurt the people inside a tank (driver and riders).
// The game applies blast damage in BasicExplosion.ExplodeDamage (a coroutine), so while that
// runs for a blast that came from a tank, hits on anyone in or on a tank are dropped.
internal static class Safety
{
	private static BasicExplosion _current;
	private static bool _currentFromTank;
	private static readonly List<Vector3> _blasts = new List<Vector3>(); // x, y, time of our shells' detonations
	private static int _logged;

	internal static void Patch(Harmony h)
	{
		int ok = 0;
		try
		{
			var it = typeof(BasicExplosion).GetNestedType("_ExplodeDamage_d__69");
			var mn = it == null ? null : AccessTools.Method(it, "MoveNext");
			if (mn != null) { h.Patch(mn, new HarmonyMethod(typeof(Safety), nameof(MoveNextPre)), new HarmonyMethod(typeof(Safety), nameof(MoveNextPost))); ok++; }
			else TLog.Warn("Safety: ExplodeDamage coroutine not found");
		}
		catch (Exception e) { TLog.Warn("Safety: blast hook failed: " + e.Message); }
		try
		{
			var hit = AccessTools.Method(typeof(HumanCharacterController), nameof(HumanCharacterController.OnHit));
			if (hit != null) { h.Patch(hit, new HarmonyMethod(typeof(Safety), nameof(OnHitPre))); ok++; }
		}
		catch (Exception e) { TLog.Warn("Safety: OnHit hook failed: " + e.Message); }
		try
		{
			var det = AccessTools.Method(typeof(RecoillessRound), nameof(RecoillessRound.Detonate));
			if (det != null) { h.Patch(det, new HarmonyMethod(typeof(Safety), nameof(DetonatePre))); ok++; }
		}
		catch (Exception e) { TLog.Warn("Safety: Detonate hook failed: " + e.Message); }
		try
		{
			var dh = AccessTools.Method(typeof(BasicGrenade), nameof(BasicGrenade.ApplyDirectHit));
			if (dh != null) { h.Patch(dh, new HarmonyMethod(typeof(Safety), nameof(DirectPre)), new HarmonyMethod(typeof(Safety), nameof(MoveNextPost))); ok++; }
		}
		catch (Exception e) { TLog.Warn("Safety: direct hit hook failed: " + e.Message); }
		TLog.Info($"Safety: {ok}/4 hooks (crew can't be hurt by tank shells: {TankPlugin.CrewSafe.Value})");
	}

	// Rounds the tank fired, so their blasts can be recognised even without an instigator.
	private static readonly HashSet<IntPtr> _ours = new HashSet<IntPtr>();
	internal static void Mark(RecoillessRound rr)
	{
		try { if (rr != null) _ours.Add(rr.Pointer); } catch { }
		if (_ours.Count > 64) _ours.Clear();
	}

	private static void DetonatePre(RecoillessRound __instance)
	{
		try
		{
			if (__instance == null || !_ours.Contains(__instance.Pointer)) return;
			Vector3 p = __instance.transform.position;
			_blasts.Add(new Vector3(p.x, p.y, Time.time));
			if (_blasts.Count > 16) _blasts.RemoveAt(0);
		}
		catch { }
	}

	private static void MoveNextPre(Il2CppSystem.Object __instance)
	{
		_current = null;
		_currentFromTank = false;
		if (!TankPlugin.CrewSafe.Value) return;
		try
		{
			var it = __instance.TryCast<BasicExplosion._ExplodeDamage_d__69>();
			BasicExplosion ex = it?.__4__this;
			if (ex == null) return;
			_current = ex;
			_currentFromTank = FromTank(ex);
		}
		catch { }
	}

	// A shell hitting someone directly (before its blast).
	private static void DirectPre(BasicGrenade __instance)
	{
		_current = null;
		_currentFromTank = false;
		if (!TankPlugin.CrewSafe.Value) return;
		try
		{
			if (__instance != null && _ours.Contains(__instance.Pointer)) { _currentFromTank = true; _directOn = true; }
		}
		catch { }
	}

	private static bool _directOn;

	private static void MoveNextPost()
	{
		_current = null;
		_currentFromTank = false;
		_directOn = false;
	}

	private static bool FromTank(BasicExplosion ex)
	{
		try
		{
			Vector2 p = ex.transform.position;
			float now = Time.time;
			foreach (Vector3 b in _blasts)
				if (now - b.z < 3f && ((Vector2)b - p).sqrMagnitude < 4f) return true;
		}
		catch { }
		try
		{
			var who = ex.instigator;
			if (who != null && InTank(who)) return true;
		}
		catch { }
		return false;
	}

	internal static bool InTank(BasicCharacterController c)
	{
		try
		{
			HumanCharacterController h = c.TryCast<HumanCharacterController>();
			if (h != null && h.drivingVehicle != null && Tank.IsTank(h.drivingVehicle)) return true;
			// riders sit inside the hull, so anyone on the tank's footprint counts
			return Tank.TankNear(c.transform.position, TankPlugin.CrewSafeRadius.Value);
		}
		catch { return false; }
	}

	private static bool OnHitPre(HumanCharacterController __instance, ref bool __result)
	{
		if ((_current == null && !_directOn) || !_currentFromTank) return true;
		if (!InTank(__instance)) return true;
		if (_logged++ < 5) TLog.Info("Safety: a tank shell hit on a crew member was blocked");
		__result = false;
		return false;
	}
}
