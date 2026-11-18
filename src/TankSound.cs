using System;
using UnityEngine;

namespace ZedTank;

// Real tank recordings, embedded as sounds/tank_<part>.wav: the engine start-up, an idle loop and
// a moving loop (engine and tracks together). They replace the base car's start-up, idle and
// driving clips, so the game still plays, fades and pitches them with the engine like any car.
internal static class TankSound
{
	private static AudioClip _start, _idle, _run;
	private static bool _made, _warned;
	private static float _pitchLo = 9f, _pitchHi, _pitchLogAt;

	private static void Make()
	{
		if (_made) return;
		_made = true;
		try
		{
			_start = Load("startup");
			_idle = Load("idle");
			_run = Load("drive");
			TLog.Info($"Tank sounds: start-up {(_start != null ? _start.length : 0):0.#} s, idle {(_idle != null ? _idle.length : 0):0.#} s, moving {(_run != null ? _run.length : 0):0.#} s");
		}
		catch (Exception e) { TLog.Warn("tank sounds: " + e.Message); }
	}

	private static AudioClip Load(string part)
	{
		using var st = typeof(TankSound).Assembly.GetManifestResourceStream($"sounds.tank_{part}.wav");
		if (st == null) return null;
		using var br = new System.IO.BinaryReader(st);
		byte[] all = br.ReadBytes((int)st.Length);
		// 16-bit PCM WAV: find the "fmt " and "data" chunks
		int ch = 1, rate = 44100, pos = 12, dataAt = -1, dataLen = 0;
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
		AudioClip c = AudioClip.Create("zt_tank_" + part, n / ch, ch, rate, false);
		c.SetData(d, 0);
		c.hideFlags = HideFlags.DontUnloadUnusedAsset;
		return c;
	}

	internal static void Tick(BasicVehicle v)
	{
		if (!TankPlugin.TankSounds.Value) return;
		Make();
		try
		{
			Engine e = v.engine;
			if (e == null) return;
			if (_start != null && (e.startUp == null || e.startUp.Pointer != _start.Pointer)) e.startUp = _start;
			if (_idle != null && (e.idleLoopClip == null || e.idleLoopClip.Pointer != _idle.Pointer)) e.idleLoopClip = _idle;
			if (_run != null && (e.engineLoopClip == null || e.engineLoopClip.Pointer != _run.Pointer)) e.engineLoopClip = _run;
			Swap(e.idleAudioSource, _idle);
			Swap(e.engineAudioSource, _run);
			PitchLog(e);
		}
		catch (Exception ex) { if (!_warned) { _warned = true; TLog.Warn("tank sounds: " + ex.Message); } }
	}

	// how far the game pitches the moving loop, to tune it if it sounds sped up
	private static void PitchLog(Engine e)
	{
		AudioSource s = e.engineAudioSource;
		if (s == null || !s.isPlaying) return;
		_pitchLo = Mathf.Min(_pitchLo, s.pitch);
		_pitchHi = Mathf.Max(_pitchHi, s.pitch);
		float now = Time.realtimeSinceStartup;
		if (now < _pitchLogAt) return;
		_pitchLogAt = now + 30f;
		TLog.Info($"Tank sounds: moving loop pitch {_pitchLo:0.##}..{_pitchHi:0.##}, volume {s.volume:0.##}");
	}

	// a source already playing the car's old clip switches to the tank's
	private static void Swap(AudioSource src, AudioClip ours)
	{
		if (src == null || ours == null || src.clip == null) return;
		if (src.clip.Pointer == ours.Pointer) return;
		bool was = src.isPlaying;
		src.clip = ours;
		if (was) src.Play();
	}

	internal static void Forget(IntPtr v) { }
}
