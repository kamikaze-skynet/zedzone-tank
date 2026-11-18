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
		string want = (TankPlugin.MgSoundWeapon.Value ?? "").Trim().ToLowerInvariant();
		if (want.Length > 0 && t.Contains(want)) return 100;
		if (t.Contains("flame") || t.Contains("laser") || t.Contains("water") || t.Contains("nail") || t.Contains("air") || t.Contains("bb ")
			|| t.Contains("loop") || t.Contains("grenade") || t.Contains("rocket") || t.Contains("taser") || t.Contains("spray") || t.Contains("foam")) return -1;
		if (t.Contains("pistol") || t.Contains("shotgun") || t.Contains("bow") || t.Contains("flare") || t.Contains("launcher") || t.Contains("revolver") || t.Contains("recoilless")) return 0;
		if (t.Contains("m249") || t.Contains("machine gun") || t.Contains("machinegun") || t.Contains("lmg") || t.Contains("m60") || t.Contains("pkm") || t.Contains("rpk") || t.Contains("minigun")) return 7;
		if (t.Contains(".50") || t.Contains("50bmg") || t.Contains("browning")) return 4;
		if (t.Contains(" mg") || t.Contains("_mg")) return 3;
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
		var seen = new System.Text.StringBuilder();
		try
		{
			var list = ItemManager.instance.itemList;
			for (int i = 0; i < list.Count; i++)
			{
				ItemAttr_RangedWeapon w = list[i]?.TryCast<ItemAttr_RangedWeapon>();
				if (w == null || string.IsNullOrEmpty(w.fireClip)) continue;
				string t = ((w.ItemName_EN ?? "") + "|" + w.fireClip).ToLowerInvariant();
				int s = ScoreMg(t);
				if (s < 0) continue;
				s += w.fullAuto ? 1 : 0;
				if (seen.Length < 1500) seen.Append($"{w.ItemName_EN} [{w.fireClip}] {s}; ");
				if (s > bs) { bs = s; best = w; }
			}
		}
		catch (Exception e) { TLog.Warn("mg sound search: " + e.Message); }
		TLog.Info("Audio: gun sounds found: " + seen);
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

	// The game's PlayAt skips a clip that is still sounding from a moment ago, so a held trigger
	// only got the odd shot. The roof gun plays through its own game audio source instead, where
	// every round starts its own copy of the clip and they overlap like a real machine gun.
	private static AudioSource _mgSrc;
	private static bool _mgSrcWarned;

	public static void Mg(Vector2 at)
	{
		AudioClip c = Mg();
		if (c == null) return;
		try
		{
			if (_mgSrc == null)
			{
				_mgSrc = AudioController.instance?.CreatePersistentAudioSource();
				if (_mgSrc != null)
				{
					_mgSrc.playOnAwake = false;
					_mgSrc.loop = false;
					_mgSrc.volume = 1f;
					TLog.Info($"Audio: machine gun source spatial {_mgSrc.spatialBlend:0.##}, mixer {(_mgSrc.outputAudioMixerGroup != null ? _mgSrc.outputAudioMixerGroup.name : "none")}");
				}
			}
			if (_mgSrc != null)
			{
				_mgSrc.transform.position = new Vector3(at.x, at.y, 0f);
				_mgSrc.pitch = Random.Range(0.96f, 1.04f);
				_mgSrc.PlayOneShot(c, 0.7f * Vol);
				return;
			}
		}
		catch (Exception e) { if (!_mgSrcWarned) { _mgSrcWarned = true; TLog.Warn("mg sound source: " + e.Message); } }
		Play(c, at, 0.7f);
	}

	public static void Cannon(Vector2 at) => Play(Cannon(), at, 1f);
}
