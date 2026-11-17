using System;
using UnityEngine;

namespace ZedTank;

// Sounds come from the game itself: the 84mm recoilless rifle's fire clip for the main gun
// and a machine gun's fire clip for the roof gun. Weapon clips are loaded on demand by the
// game (AudioController.LoadRangedWeaponClip), so they are fetched through it and played
// with AudioController.PlayAt like the game's own shots.
internal static class TankAudio
{
	private static AudioClip _mg, _cannon;
	private static bool _mgTried, _cannonTried;

	private static float Vol => Mathf.Clamp01(TankPlugin.Volume.Value);

	private static int ScoreMg(string t)
	{
		if (t.Contains("pistol") || t.Contains("shotgun") || t.Contains("bow") || t.Contains("flare") || t.Contains("launcher") || t.Contains("revolver") || t.Contains("recoilless")) return 0;
		if (t.Contains("m2 ") || t.Contains(".50") || t.Contains("50bmg") || t.Contains("browning")) return 4;
		if (t.Contains("m249") || t.Contains("machine") || t.Contains("minigun") || t.Contains("lmg") || t.Contains("m60") || t.Contains("pkm") || t.Contains("rpk") || t.Contains(" mg")) return 3;
		if (t.Contains("ak") || t.Contains("m4") || t.Contains("rifle") || t.Contains("5.56") || t.Contains("7.62")) return 2;
		return 1;
	}

	internal static AudioClip Load(ItemAttr_RangedWeapon w)
	{
		if (w == null) return null;
		string name = null;
		try { name = w.fireClip; } catch { }
		if (string.IsNullOrEmpty(name)) return null;
		AudioClip c = null;
		foreach (string slot in new[] { "fire", "Fire", "fireClip" })
		{
			try { c = AudioController.instance?.LoadRangedWeaponClip(w, slot, name); } catch { }
			if (c != null) return c;
		}
		foreach (string path in new[] { name, "Audio/" + name, "Sounds/" + name, "Audio/Weapon/" + name, "Weapon/" + name })
		{
			try { c = Resources.Load<AudioClip>(path); } catch { }
			if (c != null) return c;
		}
		try
		{
			foreach (AudioClip a in Resources.FindObjectsOfTypeAll<AudioClip>())
				if (a != null && string.Equals(a.name, name, StringComparison.OrdinalIgnoreCase)) return a;
		}
		catch { }
		return null;
	}

	private static AudioClip Cannon()
	{
		if (_cannon != null || _cannonTried) return _cannon;
		_cannonTried = true;
		_cannon = Load(Rockets.Launcher);
		TLog.Info($"Audio: main gun clip '{Rockets.FireClipName}' -> {(_cannon != null ? _cannon.name : "not found")}");
		return _cannon;
	}

	private static AudioClip Mg()
	{
		if (_mg != null || _mgTried) return _mg;
		_mgTried = true;
		ItemAttr_RangedWeapon best = null;
		int bs = -1;
		try
		{
			var list = ItemManager.instance.itemList;
			for (int i = 0; i < list.Count; i++)
			{
				ItemAttr_RangedWeapon w = list[i]?.TryCast<ItemAttr_RangedWeapon>();
				if (w == null || string.IsNullOrEmpty(w.fireClip)) continue;
				string t = ((w.ItemName_EN ?? "") + "|" + w.fireClip).ToLowerInvariant();
				int s = ScoreMg(t) + (w.fullAuto ? 1 : 0);
				if (s > bs) { bs = s; best = w; }
			}
		}
		catch (Exception e) { TLog.Warn("mg sound search: " + e.Message); }
		_mg = Load(best);
		string bn = "?";
		try { bn = best?.ItemName_EN; } catch { }
		TLog.Info($"Audio: machine gun sound from '{bn}' -> {(_mg != null ? _mg.name : "not found")}");
		return _mg;
	}

	private static void Play(AudioClip c, Vector2 at, float vol)
	{
		if (c == null) return;
		try { AudioController.instance.PlayAt(c, new Vector3(at.x, at.y, 0f), vol * Vol); }
		catch (Exception e) { TLog.Warn("sound: " + e.Message); }
	}

	public static void Mg(Vector2 at) => Play(Mg(), at, 0.7f);

	public static void Cannon(Vector2 at) => Play(Cannon(), at, 1f);
}
