using System;
using System.IO;
using System.Text;
using BepInEx;

namespace ZedTank;

// Writes to the BepInEx console/LogOutput.log and to BepInEx\Tank.log (fresh each game start).
internal static class TLog
{
	private static string FilePath => Path.Combine(Paths.BepInExRootPath, "Tank.log");
	private static bool _started;

	public static void Info(string s)
	{
		try { TankPlugin.L?.LogInfo(s.Length > 400 ? s.Substring(0, 400) : s); } catch { }
		Write(s);
	}

	public static void Warn(string s)
	{
		try { TankPlugin.L?.LogWarning(s); } catch { }
		Write("WARN " + s);
	}

	private static void Write(string s)
	{
		try
		{
			if (!_started)
			{
				_started = true;
				File.WriteAllText(FilePath, $"# Tank {TankPlugin.Version} log, started {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n", new UTF8Encoding(false));
			}
			File.AppendAllText(FilePath, $"[{DateTime.Now:HH:mm:ss}] {s}\n");
		}
		catch { }
	}
}
