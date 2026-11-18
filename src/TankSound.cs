using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZedTank;

// Tank engine and track sounds, made in code (no recorded audio). The engine clips replace the
// base car's idle and driving loops, so the game still starts, stops and pitches them with the
// engine's rpm. The tracks are an extra looping source on the tank that follows its speed.
internal static class TankSound
{
	private const int Rate = 44100;
	private static AudioClip _idle, _run, _tracks;
	private static bool _made, _warned;

	private class St { public AudioSource Tracks; public float Vol; }
	private static readonly Dictionary<IntPtr, St> _st = new Dictionary<IntPtr, St>();

	private static void Make()
	{
		if (_made) return;
		_made = true;
		try
		{
			_idle = Clip("zt_tank_idle", Engine(2f, 26f, 0.25f, 0.05f, 1));
			_run = Clip("zt_tank_engine", Engine(2f, 44f, 0.45f, 0.12f, 2));
			_tracks = Clip("zt_tank_tracks", Tracks(1f, 3));
			TLog.Info("Tank sounds made: engine idle, engine, tracks");
		}
		catch (Exception e) { TLog.Warn("tank sounds: " + e.Message); }
	}

	private static AudioClip Clip(string name, float[] data)
	{
		AudioClip c = AudioClip.Create(name, data.Length, 1, Rate, false);
		c.SetData(data, 0);
		c.hideFlags = HideFlags.DontUnloadUnusedAsset;
		return c;
	}

	// Diesel/turbine rumble: firing pulses at f Hz (whole cycles per loop so it loops cleanly),
	// low harmonics, rough low-passed noise and a faint turbine whine.
	private static float[] Engine(float secs, float f, float noise, float whine, int seed)
	{
		int n = (int)(secs * Rate);
		var rnd = new System.Random(seed);
		var d = new float[n];
		// low-passed noise, run twice around the loop so its ends join up
		var nz = new float[n];
		float lp = 0f;
		for (int pass = 0; pass < 2; pass++)
			for (int i = 0; i < n; i++)
			{
				lp += (float)(rnd.NextDouble() * 2 - 1 - lp) * 0.06f;
				nz[i] = lp;
			}
		float period = Rate / f;
		for (int i = 0; i < n; i++)
		{
			float t = i / (float)Rate;
			float ph = (i % period) / period;              // 0..1 inside one firing cycle
			float pulse = Mathf.Exp(-ph * 7f);             // a thump each cycle
			float body = 0.55f * Mathf.Sin(2 * Mathf.PI * f * t)
				+ 0.3f * Mathf.Sin(2 * Mathf.PI * f * 2 * t + 0.6f)
				+ 0.15f * Mathf.Sin(2 * Mathf.PI * f * 3 * t + 1.3f);
			float wh = whine * Mathf.Sin(2 * Mathf.PI * f * 28 * t) * (0.8f + 0.2f * Mathf.Sin(2 * Mathf.PI * 0.5f * t));
			d[i] = (0.5f * body + 0.5f * pulse * body + noise * nz[i] * 4f * (0.6f + 0.4f * pulse) + wh);
		}
		Normalize(d, 0.8f);
		return d;
	}

	// Track clatter: 14 link clanks a second (whole number per loop), each a short metallic ring
	// with a click, over a low grinding rumble.
	private static float[] Tracks(float secs, int seed)
	{
		int n = (int)(secs * Rate);
		var rnd = new System.Random(seed);
		var d = new float[n];
		float lp = 0f;
		var grind = new float[n];
		for (int pass = 0; pass < 2; pass++)
			for (int i = 0; i < n; i++)
			{
				lp += (float)(rnd.NextDouble() * 2 - 1 - lp) * 0.03f;
				grind[i] = lp;
			}
		int clanks = 14;
		float[] freqs = { 1150f, 1730f, 2610f, 3900f };
		for (int k = 0; k < clanks; k++)
		{
			int start = (int)(k * n / (float)clanks + (rnd.NextDouble() - 0.5) * 0.012 * Rate);
			float amp = 0.6f + (float)rnd.NextDouble() * 0.4f;
			float tune = 0.92f + (float)rnd.NextDouble() * 0.16f;
			int len = (int)(0.07f * Rate);
			for (int j = 0; j < len; j++)
			{
				int i = ((start + j) % n + n) % n;
				float t = j / (float)Rate;
				float env = Mathf.Exp(-t * 60f);
				float ring = 0f;
				for (int q = 0; q < freqs.Length; q++) ring += Mathf.Sin(2 * Mathf.PI * freqs[q] * tune * t) / (q + 1);
				float click = j < 90 ? (float)(rnd.NextDouble() * 2 - 1) * (1f - j / 90f) : 0f;
				d[i] += amp * (0.45f * ring * env + 0.6f * click);
			}
		}
		for (int i = 0; i < n; i++)
		{
			float t = i / (float)Rate;
			d[i] += grind[i] * 5f + 0.15f * Mathf.Sin(2 * Mathf.PI * 14f * 3 * t);
		}
		Normalize(d, 0.75f);
		return d;
	}

	private static void Normalize(float[] d, float peak)
	{
		float m = 0f;
		foreach (float x in d) m = Mathf.Max(m, Mathf.Abs(x));
		if (m <= 0f) return;
		float k = peak / m;
		for (int i = 0; i < d.Length; i++) d[i] *= k;
	}

	internal static void Tick(BasicVehicle v)
	{
		if (!TankPlugin.TankSounds.Value) return;
		Make();
		if (_idle == null) return;
		try
		{
			Engine e = v.engine;
			if (e == null) return;
			if (e.idleLoopClip == null || e.idleLoopClip.Pointer != _idle.Pointer) e.idleLoopClip = _idle;
			if (e.engineLoopClip == null || e.engineLoopClip.Pointer != _run.Pointer) e.engineLoopClip = _run;
			Swap(e.idleAudioSource, _idle, e.idleLoopClip);
			Swap(e.engineAudioSource, _run, e.engineLoopClip);

			if (!_st.TryGetValue(v.Pointer, out St s) || s.Tracks == null)
			{
				s = new St();
				AudioSource src = v.gameObject.AddComponent<AudioSource>();
				AudioSource like = e.engineAudioSource ?? e.idleAudioSource;
				if (like != null)
				{
					src.outputAudioMixerGroup = like.outputAudioMixerGroup;
					src.spatialBlend = like.spatialBlend;
					src.rolloffMode = like.rolloffMode;
					src.minDistance = like.minDistance;
					src.maxDistance = like.maxDistance;
					src.dopplerLevel = 0f;
				}
				src.clip = _tracks;
				src.loop = true;
				src.playOnAwake = false;
				src.volume = 0f;
				s.Tracks = src;
				_st[v.Pointer] = s;
				TLog.Info($"Tank sounds on '{v.name}': tracks source spatial {src.spatialBlend:0.##}, mixer {(src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name : "none")}");
			}
			float speed = 0f;
			try { if (v.m_rigidbody != null) speed = v.m_rigidbody.velocity.magnitude; } catch { }
			float top = 12f;
			float want = Mathf.Clamp01(speed / 2.5f) * Mathf.Clamp01(TankPlugin.TrackVolume.Value);
			s.Vol = Mathf.MoveTowards(s.Vol, want, Time.deltaTime * 2f);
			s.Tracks.volume = s.Vol;
			s.Tracks.pitch = 0.55f + Mathf.Clamp01(speed / top) * 0.9f;
			if (s.Vol > 0.005f) { if (!s.Tracks.isPlaying) s.Tracks.Play(); }
			else if (s.Tracks.isPlaying) s.Tracks.Stop();
		}
		catch (Exception ex) { if (!_warned) { _warned = true; TLog.Warn("tank sounds: " + ex.Message); } }
	}

	// a source already playing the car's old clip switches to the tank's
	private static void Swap(AudioSource src, AudioClip ours, AudioClip want)
	{
		if (src == null || want == null) return;
		if (src.clip != null && src.clip.Pointer == ours.Pointer) return;
		if (src.clip == null) return;
		bool was = src.isPlaying;
		float t = 0f;
		try { t = src.time; } catch { }
		src.clip = ours;
		if (was) { src.Play(); try { src.time = t % ours.length; } catch { } }
	}

	internal static void Forget(IntPtr v)
	{
		if (_st.TryGetValue(v, out St s))
		{
			try { if (s.Tracks != null) UnityEngine.Object.Destroy(s.Tracks); } catch { }
			_st.Remove(v);
		}
	}
}
