using System;
using System.Text;
using UnityEngine;
using UnityEngine.Audio;

namespace ZedTank;

// Sounds come from the game itself: a machine gun's fire clip for the roof gun and the
// recoilless launcher's (or the heaviest gun's) fire clip for the main gun.
internal static class TankAudio
{
	private static bool _init;
	private static AudioClip _mg;
	private static AudioClip _cannon;
	private static AudioMixerGroup _group;
	private static AudioSource _src;

	private static float Vol => Mathf.Clamp01(TankPlugin.Volume.Value);

	private static int ScoreMg(string t)
	{
		if (t.Contains("pistol") || t.Contains("shotgun") || t.Contains("bow") || t.Contains("flare") || t.Contains("launcher") || t.Contains("revolver")) return 0;
		if (t.Contains("m2") || t.Contains("50bmg") || t.Contains("browning")) return 4;
		if (t.Contains("m249") || t.Contains("mg") || t.Contains("machine") || t.Contains("minigun") || t.Contains("lmg") || t.Contains("m60") || t.Contains("pkm") || t.Contains("rpk")) return 3;
		if (t.Contains("ak") || t.Contains("m4") || t.Contains("rifle") || t.Contains("556") || t.Contains("762")) return 2;
		return 1;
	}

	private static int ScoreCannon(string t)
	{
		if (t.Contains("recoilless") || t.Contains("carl") || t.Contains("gustaf") || t.Contains("rpg") || t.Contains("rocket")) return 5;
		if (t.Contains("launcher") || t.Contains("cannon") || t.Contains("grenade")) return 4;
		if (t.Contains("50bmg") || t.Contains("barrett") || t.Contains("m82")) return 3;
		if (t.Contains("shotgun") || t.Contains("magnum") || t.Contains("sniper")) return 2;
		return 0;
	}

	private static void Init()
	{
		if (_init) return;
		_init = true;
		var sb = new StringBuilder("Audio: ");
		try
		{
			int bm = -1, bc = 0;
			foreach (BasicRangedWeapon w in Resources.FindObjectsOfTypeAll<BasicRangedWeapon>())
			{
				if (w == null || w.fireClip == null) continue;
				string t = (w.name + "|" + w.fireClip.name).ToLowerInvariant();
				int m = ScoreMg(t);
				if (m > bm) { bm = m; _mg = w.fireClip; }
				int c = ScoreCannon(t);
				if (c > bc) { bc = c; _cannon = w.fireClip; }
			}
		}
		catch (Exception e) { sb.Append("guns " + e.Message + "; "); }
		try
		{
			string want = Rockets.FireClipName;
			if (!string.IsNullOrEmpty(want))
			{
				foreach (AudioClip a in Resources.FindObjectsOfTypeAll<AudioClip>())
				{
					if (a != null && string.Equals(a.name, want, StringComparison.OrdinalIgnoreCase)) { _cannon = a; break; }
				}
			}
		}
		catch { }
		try
		{
			int best = -1;
			foreach (AudioSource s in Resources.FindObjectsOfTypeAll<AudioSource>())
			{
				AudioMixerGroup g = s != null ? s.outputAudioMixerGroup : null;
				if (g == null) continue;
				string t = (g.name ?? "").ToLowerInvariant();
				int k = 1;
				if (t.Contains("music") || t.Contains("bgm") || t.Contains("ui") || t.Contains("voice") || t.Contains("ambient")) k = 0;
				if (t.Contains("sfx") || t.Contains("effect") || t.Contains("sound") || t.Contains("world")) k = 2;
				if (k > best) { best = k; _group = g; }
			}
		}
		catch { }
		sb.Append($"mg={(_mg != null ? _mg.name : "none")} cannon={(_cannon != null ? _cannon.name : "none")} mixer={(_group != null ? _group.name : "none")}");
		TLog.Info(sb.ToString());
	}

	private static AudioSource Src()
	{
		if (_src != null) return _src;
		var go = new GameObject("zt_tank_audio");
		Object.DontDestroyOnLoad(go);
		go.hideFlags = HideFlags.HideAndDontSave;
		_src = go.AddComponent<AudioSource>();
		_src.playOnAwake = false;
		_src.spatialBlend = 0f;
		if (_group != null) _src.outputAudioMixerGroup = _group;
		return _src;
	}

	private static float Falloff(Vector2 at)
	{
		try
		{
			float d = ((Vector2)GameController.instance.playerCharacter.transform.position - at).magnitude;
			return Mathf.Clamp01(1f - d / 50f);
		}
		catch { return 1f; }
	}

	public static void Mg(Vector2 at)
	{
		Init();
		try { if (_mg != null) Src().PlayOneShot(_mg, Vol * 0.6f * Falloff(at)); } catch { }
	}

	public static void Cannon(Vector2 at)
	{
		Init();
		try
		{
			if (_cannon == null) return;
			Src().PlayOneShot(_cannon, Vol * Falloff(at));
		}
		catch { }
	}
}
