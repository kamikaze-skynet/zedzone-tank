using System;
using System.IO;
using UnityEngine;

namespace ZedTank;

// The 105 mm shell is a game item mod (.zedmod) shipped in an "items" folder next to Tank.dll.
// The game's own mod loader only scans its Mods / Workshop content folders, so we load it here
// right after the game has loaded its mods (and once more later if that hook ran too late).
internal static class Bundled
{
	internal const string ShellGuid = "3f7c1d05-a105-4c2b-9e10-105a7a4c0105";
	private static bool _done;

	internal static void LoadMissing(string when)
	{
		if (_done) return;
		string dir;
		try { dir = Path.Combine(Path.GetDirectoryName(typeof(TankPlugin).Assembly.Location) ?? "", "items"); }
		catch { return; }
		if (!Directory.Exists(dir)) { _done = true; TLog.Warn("Bundled items: no items folder next to Tank.dll (" + dir + ")"); return; }

		var loaded = ModLoader.loadedMods;
		var handlers = ModLoader.handlers;
		if (handlers == null || handlers.Count == 0) return; // not ready yet, try again later
		_done = true;

		int ok = 0, skipped = 0, failed = 0;
		foreach (string sub in Directory.GetDirectories(dir))
		{
			string folder = Path.GetFileName(sub);
			IModHandler handler = null;
			foreach (var kv in handlers)
			{
				var h = kv.Value;
				if (h == null) continue;
				string hs = null, ht = null;
				try { hs = h.SubFolder; ht = h.ModType; } catch { }
				if (string.Equals(hs, folder, StringComparison.OrdinalIgnoreCase) || string.Equals(ht, folder, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(kv.Key, folder, StringComparison.OrdinalIgnoreCase)) { handler = h; break; }
			}
			if (handler == null) { TLog.Warn($"Bundled items: no game mod handler for '{folder}'"); continue; }
			foreach (string file in Directory.GetFiles(sub, "*.zedmod"))
			{
				string guid = Path.GetFileNameWithoutExtension(file);
				if (loaded != null && loaded.ContainsKey(guid)) { skipped++; continue; }
				try
				{
					if (ModLoader.TryLoadZedmod(file, handler)) ok++;
					else { failed++; TLog.Warn("Bundled items: the game refused " + Path.GetFileName(file)); }
				}
				catch (Exception e) { failed++; TLog.Warn("Bundled items: loading " + Path.GetFileName(file) + " failed: " + e.Message); }
			}
		}
		TLog.Info($"Bundled items ({when}): {ok} loaded, {skipped} already loaded by the game, {failed} failed. 105 mm shell id {ShellId()}.");
		if (ok > 0)
		{
			try { ModLocaleManager.ReapplyAllLocales(); } catch { }
		}
	}

	/// <summary>Runtime item id of the 105 mm shell, or -1 if the game hasn't loaded it.</summary>
	internal static int ShellId()
	{
		try
		{
			var mods = ModLoader.loadedMods;
			if (mods != null && mods.ContainsKey(ShellGuid))
			{
				var info = mods[ShellGuid];
				if (info != null && info.resolvedRuntimeId > 0) return info.resolvedRuntimeId;
			}
		}
		catch { }
		return -1;
	}
}
