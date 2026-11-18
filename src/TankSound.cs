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

	private static string _style, _raw;

	// Loops made offline by art-src/tank_sounds.py and embedded as sounds.<style>_<part>.wav.
	private static void Make()
	{
		string raw = TankPlugin.SoundStyle.Value ?? "diesel";
		if (_made && ReferenceEquals(raw, _raw)) return;
		_raw = raw;
		string style = raw.Trim().ToLowerInvariant().Replace(' ', '_');
		if (_made && style == _style) return;
		_made = true;
		_style = style;
		try
		{
			_idle = Load(style, "idle");
			_run = Load(style, "drive");
			_tracks = Load(style, "tracks");
			if (_idle == null || _run == null || _tracks == null)
			{
				TLog.Warn($"Tank sounds: style '{style}' not found (diesel, heavy_diesel, turbine); using diesel");
				_idle = Load("diesel", "idle"); _run = Load("diesel", "drive"); _tracks = Load("diesel", "tracks");
			}
			foreach (St st in _st.Values) if (st.Tracks != null) { st.Tracks.clip = _tracks; }
			TLog.Info($"Tank sounds: style '{style}'");
		}
		catch (Exception e) { TLog.Warn("tank sounds: " + e.Message); }
	}

	private static AudioClip Load(string style, string part)
	{
		using var st = typeof(TankSound).Assembly.GetManifestResourceStream($"sounds.{style}_{part}.wav");
		if (st == null) return null;
		using var br = new System.IO.BinaryReader(st);
		byte[] all = br.ReadBytes((int)st.Length);
		// 16-bit PCM WAV: find the "fmt " and "data" chunks
		int ch = 1, rate = Rate, pos = 12, dataAt = -1, dataLen = 0;
		while (pos + 8 <= all.Length)
		{
			string id = System.Text.Encoding.ASCII.GetString(all, pos, 4);
			int len = BitConverter.ToInt32(all, pos + 4);
			if (id == "fmt ") { ch = BitConverter.ToInt16(all, pos + 10); rate = BitConverter.ToInt32(all, pos + 12); }
			else if (id == "data") { dataAt = pos + 8; dataLen = Math.Min(len, all.Length - dataAt); break; }
			pos += 8 + len + (len & 1);
		}
		if (dataAt < 0) return null;
		int n = dataLen / 2;
		var d = new float[n];
		for (int i = 0; i < n; i++) d[i] = BitConverter.ToInt16(all, dataAt + i * 2) / 32768f;
		AudioClip c = AudioClip.Create($"zt_tank_{style}_{part}", n / ch, ch, rate, false);
		c.SetData(d, 0);
		c.hideFlags = HideFlags.DontUnloadUnusedAsset;
		return c;
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
