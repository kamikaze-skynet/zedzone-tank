using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ZedTank;

// The tank is one of the game's own cars (so it drives, takes damage, carries cargo and
// burns gasoline exactly like the game's cars) with its look replaced by a hull, a turret
// that turns toward the mouse and a roof machine gun: a base car is tagged with a name id,
// its renderers are hidden, and tagged cars are remembered per save.
internal static partial class Tank
{
	public const int NameId = 88040;
	public const int NameId3D = 88041; // the Battle Tank: a real 3D model (src/Model3D.cs)

	// The art is a 3D block model pre-rendered from the game's tilted overhead view at 32
	// headings (art-src/render_angles.py), like the game's cars, which are 3D models filmed
	// from above. Hull and turret are separate sheets of 8 x 4 frames; frame i faces
	// i * 11.25 degrees. Both share the same ground origin at each frame's centre (the turret
	// ring sits at the hull's centre).
	private const float Ppu = 32f;
	private const int Frames = 32;
	private const int HullCell = 216, TurretCell = 360;
	private const float PxPerDm = 2.3f;                     // render scale: pixels per decimetre
	private static readonly float Tilt = 45f * Mathf.Deg2Rad;  // render camera tilt from straight down (the game's car camera is 45)
	private static readonly Vector3 BarrelTip = new Vector3(76f, 0f, 18f);   // model space, decimetres
	private static readonly Vector3 MgTip = new Vector3(12f, 7f, 23f);
	private static readonly Vector3 RingTop = new Vector3(0f, 0f, 18f);

	private class Vis
	{
		public GameObject Root;
		public SpriteRenderer Hull;
		public SpriteRenderer HullShadow;
		public SpriteRenderer Turret;
		public float TurretRel;   // turret angle relative to the hull (degrees)
		public float TurretWorld; // turret world angle (degrees), updated each frame
		public float Heading;
		public readonly List<Renderer> Hidden = new List<Renderer>();
		public float NextHide;
	}

	private static readonly CultureInfo CI = CultureInfo.InvariantCulture;
	private static readonly Dictionary<IntPtr, Vis> _vis = new Dictionary<IntPtr, Vis>();
	private static readonly List<Vector2> _known = new List<Vector2>();
	private static readonly List<bool> _known3 = new List<bool>(); // same index: is it a Battle Tank (3D)
	private static readonly Dictionary<IntPtr, int> _knownIdx = new Dictionary<IntPtr, int>();

	private static long _saveId = -1;
	private static IntPtr _gdPtr;
	private static int _called;
	private static bool _dirty;
	private static float _nextSave;
	private static float _scanAt;
	private static float _callWait;
	private static float _cannonAt;
	private static float _mgAt;
	// The base car's nose direction in its own space. It's learned from how the car actually
	// moves while you hold W (or S) and cached in Tank.forward.txt. 0 until learned.
	private static float _fwd = 0f;
	private static int _fwdVotes;
	private static bool _fwdKnown;
	private static int _baseId = -1;
	private static int _sortLayer;
	private static bool _sortSet;
	internal static bool SortKnown => _sortSet;
	internal static int SortLayer => _sortLayer;
	private static Sprite[] _hullFr, _turretFr;
	private static bool _spriteTried;
	private static string _msg;
	private static float _msgUntil;
	private static int _hitLog, _retagLog, _spawnLog;
	private static BasicVehicle _mine;

	internal static string Status;

	/// <summary>True while the local player is driving a tank (mouse aims the turret).</summary>
	public static bool Crewing => _mine != null;

	private static string FilePath => Path.Combine(Paths.ConfigPath, $"Tank.{_saveId}.txt");
	private static string FwdPath => Path.Combine(Paths.ConfigPath, "Tank.forward.txt");

	private static void LoadForward()
	{
		try
		{
			if (_fwdKnown || !File.Exists(FwdPath)) return;
			string[] a = File.ReadAllText(FwdPath).Trim().Split('|');
			// v2 = learned from driving (older files came from the headlights and could be backwards)
			if (a.Length >= 3 && a[0] == "v2" && int.Parse(a[1], CI) == BaseId())
			{
				_fwd = F(a[2]);
				_fwdKnown = true;
				TLog.Info($"Base car nose direction {_fwd:0} (cached)");
			}
		}
		catch { }
	}

	// ------------------------------------------------------------------ messages

	private static void Msg(string s)
	{
		_msg = s;
		_msgUntil = Time.realtimeSinceStartup + 4f;
	}

	public static string Message() => Time.realtimeSinceStartup < _msgUntil ? _msg : null;

	internal static string Ok(string m) { Msg(m); return m; }

	internal static string Fail(string m) { Msg(m); return "!" + m; }

	// ------------------------------------------------------------------ identity

	public static bool IsTank(BasicVehicle v)
	{
		try
		{
			if (v == null) return false;
			int id = v.vehicleNameTextId;
			if (id == NameId || id == NameId3D) return true;
			VehicleData d = v.vehicleData;
			return d != null && (d.vehicleNameTextId == NameId || d.vehicleNameTextId == NameId3D);
		}
		catch { return false; }
	}

	public static bool Is3D(BasicVehicle v)
	{
		try
		{
			if (v == null) return false;
			if (v.vehicleNameTextId == NameId3D) return true;
			VehicleData d = v.vehicleData;
			return d != null && d.vehicleNameTextId == NameId3D;
		}
		catch { return false; }
	}

	private static void Tag(BasicVehicle v, bool d3)
	{
		int id = d3 ? NameId3D : NameId;
		try { v.vehicleNameTextId = id; } catch { }
		try { if (v.vehicleData != null) v.vehicleData.vehicleNameTextId = id; } catch { }
		Toughen(v);
	}

	// Crew inside take much less damage than in a car.
	private static void Toughen(BasicVehicle v)
	{
		try { v.inVehicleCharacterDamageTakenFloat = Mathf.Clamp01(TankPlugin.CrewDamage.Value); } catch { }
	}

	// The base car: the game's fuel-powered car with the most seats (then the biggest cargo hold).
	private static int BaseId()
	{
		if (_baseId >= 0) return _baseId;
		GameController gc = GameController.instance;
		var sb = new StringBuilder();
		int best = -1;
		float bestScore = -1f;
		int n = 64;
		try { if (gc.vehiclePrefabs != null) n = Mathf.Max(gc.vehiclePrefabs.Length, 1); } catch { }
		for (int i = 0; i < n + 4; i++)
		{
			try
			{
				if (!gc.IsValidVehicleId(i)) continue;
				BasicVehicle p = gc.GetVehiclePrefabConfig(i);
				if (p == null) continue;
				string name = null;
				try { name = TextManager.instance?.GetText(p.vehicleNameTextId); } catch { }
				Vector2Int inv = p.inventorySize;
				float score = p.passengerSeatLimit * 100f + inv.x * inv.y;
				bool fuel = (int)p.powerType == 0;
				if (sb.Length < 900) sb.Append($"{i} '{name}' seats {p.passengerSeatLimit} cargo {inv.x}x{inv.y} {p.powerType}; ");
				if (fuel && i != GameController.forkliftVehicleId && score > bestScore)
				{
					bestScore = score;
					best = i;
				}
			}
			catch { }
		}
		TLog.Info($"Cars: {sb}-> tank base car {best}");
		_baseId = best;
		return best;
	}

	// ------------------------------------------------------------------ save file

	private static float F(string s) => float.Parse(s, NumberStyles.Float, CI);

	private static void Load()
	{
		_called = 0;
		_known.Clear();
		_known3.Clear();
		_knownIdx.Clear();
		ClearVis();
		_mine = null;
		try
		{
			if (!File.Exists(FilePath)) return;
			foreach (string line in File.ReadAllLines(FilePath))
			{
				string[] a = line.Split('|');
				if (a[0] == "K" && a.Length >= 3) { _known.Add(new Vector2(F(a[1]), F(a[2]))); _known3.Add(a.Length >= 4 && a[3] == "3"); }
				else if (a[0] == "C" && a.Length >= 2) _called = int.Parse(a[1], CI);
				else if (a[0] == "R2" && a.Length >= 2) { _fwd = F(a[1]); _fwdKnown = true; }
			}
			TLog.Info($"Save {_saveId}: {_known.Count} tank(s) on record, {_called} called in");
		}
		catch (Exception e) { TLog.Warn("load: " + e.Message); }
	}

	private static void Save()
	{
		try
		{
			var sb = new StringBuilder();
			sb.AppendLine("C|" + _called.ToString(CI));
			if (_fwdKnown) sb.AppendLine("R2|" + _fwd.ToString("0", CI));
			for (int i = 0; i < _known.Count; i++)
			{
				Vector2 k = _known[i];
				sb.AppendLine($"K|{k.x.ToString("0.##", CI)}|{k.y.ToString("0.##", CI)}|{(i < _known3.Count && _known3[i] ? "3" : "1")}");
			}
			File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
			_dirty = false;
		}
		catch (Exception e) { TLog.Warn("save: " + e.Message); }
	}

	public static void OnGameSave()
	{
		try
		{
			if (_saveId < 0) return;
			Track();
			Save();
		}
		catch { }
	}

	// Remembers where every tank is, and after a reload re-tags the base car parked there.
	private static void Track()
	{
		var live = BasicVehicle.s_liveVehicles;
		if (live == null) return;
		var alive = new HashSet<IntPtr>();
		for (int i = 0; i < live.Count; i++) if (live[i] != null) alive.Add(live[i].Pointer);
		var gone = new List<IntPtr>();
		foreach (IntPtr k in _knownIdx.Keys) if (!alive.Contains(k)) gone.Add(k);
		foreach (IntPtr k in gone) _knownIdx.Remove(k);
		var used = new HashSet<int>(_knownIdx.Values);
		for (int i = 0; i < live.Count; i++)
		{
			BasicVehicle v = live[i];
			if (v == null || !IsTank(v)) continue;
			Vector2 p = VehPos(v);
			if (!_knownIdx.TryGetValue(v.Pointer, out int idx))
			{
				idx = Nearest(p, 3f, used);
				if (idx < 0)
				{
					_known.Add(p);
					_known3.Add(Is3D(v));
					idx = _known.Count - 1;
					_dirty = true;
				}
				_knownIdx[v.Pointer] = idx;
				used.Add(idx);
				Toughen(v);
			}
			if ((_known[idx] - p).sqrMagnitude > 0.25f)
			{
				_known[idx] = p;
				_dirty = true;
			}
		}
		int baseId = BaseId();
		for (int i = 0; i < live.Count; i++)
		{
			BasicVehicle v = live[i];
			if (v == null || IsTank(v) || _knownIdx.ContainsKey(v.Pointer)) continue;
			VehicleData d = null;
			try { d = v.vehicleData; } catch { }
			if (d == null || d.vehicleId != baseId) continue;
			try { if (d.vehicleNameTextId == 88030) continue; } catch { } // the Helicopter mod's
			Vector2 p = VehPos(v);
			int idx = Nearest(p, 2.5f, used);
			if (idx < 0) continue;
			Tag(v, idx < _known3.Count && _known3[idx]);
			_knownIdx[v.Pointer] = idx;
			used.Add(idx);
			if (_retagLog++ < 8) TLog.Info($"Re-tagged the tank at {p.x:0.#},{p.y:0.#} after loading");
		}
	}

	private static int Nearest(Vector2 p, float r, HashSet<int> skip)
	{
		int best = -1;
		float bd = r * r;
		for (int i = 0; i < _known.Count; i++)
		{
			if (skip.Contains(i)) continue;
			float d = (_known[i] - p).sqrMagnitude;
			if (d < bd) { bd = d; best = i; }
		}
		return best;
	}

	// ------------------------------------------------------------------ spawning

	private static BasicChunk ChunkAt(Vector2 pos)
	{
		try
		{
			MapController m = MapController.instance;
			return m.GetChunkByCoordinate(m.GetChunkCoordinateByPosition(pos));
		}
		catch { return null; }
	}

	internal static bool TankNear(Vector2 pos, float r)
	{
		try
		{
			var live = BasicVehicle.s_liveVehicles;
			if (live == null) return false;
			for (int i = 0; i < live.Count; i++)
			{
				if (IsTank(live[i]) && ((Vector2)live[i].transform.position - pos).sqrMagnitude < r * r) return true;
			}
		}
		catch { }
		return false;
	}

	private static BasicVehicle Spawn(Vector2 pos, float heading, bool d3)
	{
		GameController gc = GameController.instance;
		int baseId = BaseId();
		if (baseId < 0) { TLog.Warn("no base car found"); return null; }
		BasicChunk chunk = ChunkAt(pos);
		ChunkData cd = chunk != null ? chunk.chunkData : null;
		if (cd == null) { TLog.Warn($"spawn: chunk at {pos} not loaded"); return null; }
		VehicleData vd;
		try { vd = gc.GetPristineVehicleData(baseId, heading); }
		catch (Exception e) { TLog.Warn("GetPristineVehicleData: " + e.Message); return null; }
		if (vd == null) return null;
		vd.vehicleNameTextId = d3 ? NameId3D : NameId;
		try { vd.worldCoordinate = pos; } catch { }
		try { vd.coordinateX = pos.x; vd.coordinateY = pos.y; } catch { }
		try { vd.eularAngleZ = heading; } catch { }
		try { vd.zLevel = gc.playerCharacter.characterData.zLevel; } catch { }
		SetFuel(vd, Cap(vd) * Mathf.Clamp01(TankPlugin.StartFuel.Value));
		try { if (cd.vehicleDataList != null && !cd.vehicleDataList.Contains(vd)) cd.vehicleDataList.Add(vd); } catch { }
		BasicVehicle v = null;
		string how = "";
		try
		{
			InGameController igc = InGameController.instance;
			if (igc != null) { v = igc.AddVehicle(vd, chunk); how = "AddVehicle"; }
		}
		catch (Exception e) { how = "AddVehicle failed " + e.Message + "; "; }
		if (v == null)
		{
			try { v = cd.SpawnVehicleOnLayer(vd); how += "SpawnVehicleOnLayer"; }
			catch (Exception e) { how += "SpawnVehicleOnLayer failed " + e.Message; }
		}
		if (v == null)
		{
			try { cd.vehicleDataList.Remove(vd); } catch { }
		}
		else
		{
			Tag(v, d3);
			try
			{
				v.transform.position = new Vector3(pos.x, pos.y, v.transform.position.z);
				if (v.m_rigidbody != null) v.m_rigidbody.position = pos;
			}
			catch { }
			try { var l = chunk.vehicleList; if (l != null && !l.Contains(v)) l.Add(v); } catch { }
			try { if (!v.initExecutedFlag) v.Init(); } catch { }
			try
			{
				VehiclePartData t = v.fuelTankData ?? FuelPart(v.vehicleData);
				if (t != null) t.fuel = Cap(v.vehicleData) * Mathf.Clamp01(TankPlugin.StartFuel.Value);
			}
			catch { }
			_known.Add(pos);
			_known3.Add(d3);
			_knownIdx[v.Pointer] = _known.Count - 1;
			_dirty = true;
		}
		if (_spawnLog++ < 8) TLog.Info($"Spawn {(d3 ? "Battle Tank (3D)" : "tank")} (base car {baseId}, {how}) at {pos.x:0.#},{pos.y:0.#}: {(v != null ? $"ok, fuel {Fuel(v):0.#}/{Cap(v.vehicleData):0} L" : "FAILED")}");
		return v;
	}

	internal static string CallIn(HumanCharacterController pc, bool ignoreLimit, bool useKit = false, bool battle = false)
	{
		if (pc == null) return Fail("Load into a world first");
		try { if (pc.drivingVehicle != null) return Fail("Get out of the vehicle first"); } catch { }
		if (!Sprites()) return Fail("Tank art failed to load (see BepInEx\\Tank.log)");
		Vector2 here = pc.transform.position;
		if (!ignoreLimit && TankNear(here, TankPlugin.NearbyRadius.Value)) return Fail("There's already a tank close by");
		// With a crafted Tank Kit the kit is the cost, so the per-save limit doesn't apply.
		// A Battle Tank Kit (3D model) is used first when you carry both kinds.
		int kitId = -1;
		bool d3 = battle;
		if (useKit)
		{
			int k3 = Bundled.Kit3DId(), k1 = Bundled.KitId();
			var carried = Carried(pc);
			if (k3 > 0 && Count(carried, k3) >= 1) { kitId = k3; d3 = true; }
			else if (k1 > 0 && Count(carried, k1) >= 1) { kitId = k1; d3 = false; }
			else if (k1 <= 0 && k3 <= 0) return Fail("The Tank Kit items didn't load (see BepInEx\\Tank.log)");
			else return Fail(TankPlugin.HideClassic.Value ? "You need a Battle Tank Kit: build one at a workbench and carry it" : "You need a Tank Kit or a Battle Tank Kit: build one at a workbench and carry it");
		}
		if (TankPlugin.HideClassic.Value) d3 = true;
		int max = TankPlugin.MaxTanks.Value;
		if (!ignoreLimit && !useKit && max > 0 && _called >= max) return Fail($"This save has already called in {max} tank(s) (setting MaxTanks)");
		Vector2 spot = here + new Vector2(0f, -5f);
		if (Spawn(spot, 0f, d3) != null)
		{
			if (useKit && AutoIO.Take(Carried(pc), kitId, 1f) < 1f) TLog.Warn("Tank Kit: tank delivered but the kit couldn't be taken");
			_called++;
			Save();
			return Ok((d3 ? "Battle Tank" : "Tank") + " delivered. Get in, start the engine and roll out.");
		}
		return Fail("No room for a tank here, try an open spot");
	}

	private static void CallKey(HumanCharacterController pc)
	{
		KeyCode key = TankPlugin.CallKey.Value;
		if (key == KeyCode.None || !Input.GetKeyDown(key)) return;
		if (Time.realtimeSinceStartup < _callWait) return;
		_callWait = Time.realtimeSinceStartup + 1.5f;
		if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
		{
			Refuel(pc);
			return;
		}
		bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
		CallIn(pc, false, TankPlugin.RequireKit.Value, ctrl); // without kits, Ctrl+Page Up brings the Battle Tank
	}

	// ------------------------------------------------------------------ fuel (the game's car fuel)

	private static VehiclePartData FuelPart(VehicleData vd)
	{
		try
		{
			var parts = vd?.vehiclePartDatas;
			if (parts == null) return null;
			for (int i = 0; i < parts.Count; i++) if (parts[i] != null && (int)parts[i].vehiclePartType == 4) return parts[i];
		}
		catch { }
		return null;
	}

	private static void SetFuel(VehicleData vd, float l)
	{
		VehiclePartData t = FuelPart(vd);
		if (t != null) t.fuel = Mathf.Clamp(l, 0f, Cap(vd));
	}

	internal static float Cap(VehicleData vd)
	{
		try { if (vd.vehicleFuelTankSize > 1f) return vd.vehicleFuelTankSize; } catch { }
		return 60f;
	}

	internal static float Fuel(BasicVehicle v)
	{
		try
		{
			VehiclePartData t = v.fuelTankData ?? FuelPart(v.vehicleData);
			return t != null ? t.fuel : 0f;
		}
		catch { return 0f; }
	}

	internal static string Refuel(HumanCharacterController pc)
	{
		BasicVehicle v = null;
		try { v = pc.drivingVehicle; } catch { }
		if (!IsTank(v)) v = NearestTank(pc.transform.position, 6f);
		if (v == null) return Fail("No tank close by to refuel");
		float cap = Cap(v.vehicleData), before = Fuel(v);
		if (cap - before < 0.05f) return Fail("The tank is already full");
		VehiclePartData tank = null;
		try { tank = v.fuelTankData ?? FuelPart(v.vehicleData); } catch { }
		if (tank == null) return Fail("This tank has no fuel tank part");
		foreach (InventoryData inv in Carried(pc))
		{
			var items = inv.itemList;
			if (items == null) continue;
			for (int i = 0; i < items.Count && Fuel(v) < cap - 0.05f; i++)
			{
				ItemData it = items[i];
				if (it == null) continue;
				try
				{
					if (!VehicleFuelTransfer.IsFuelContainer(it)) continue;
					if (VehicleFuelTransfer.AddFuelFromContainer(v.vehicleData, tank, it) > 0f)
					{
						try { inv.RefreshTotalItemWeight(); } catch { }
					}
				}
				catch (Exception e) { TLog.Warn("refuel: " + e.Message); }
			}
		}
		float after = Fuel(v);
		if (after - before < 0.01f) return Fail("You aren't carrying any gasoline");
		return Ok($"Added {after - before:0.#} L of gasoline ({after:0.#} / {cap:0} L)");
	}

	private static BasicVehicle NearestTank(Vector2 pos, float r)
	{
		BasicVehicle best = null;
		float bd = r * r;
		try
		{
			var live = BasicVehicle.s_liveVehicles;
			if (live == null) return null;
			for (int i = 0; i < live.Count; i++)
			{
				BasicVehicle v = live[i];
				if (!IsTank(v)) continue;
				float d = ((Vector2)v.transform.position - pos).sqrMagnitude;
				if (d <= bd) { bd = d; best = v; }
			}
		}
		catch { }
		return best;
	}

	internal static List<InventoryData> Carried(HumanCharacterController pc)
	{
		var list = new List<InventoryData>();
		try
		{
			var l = pc.characterData.GetCarriedInventoryList();
			if (l != null) for (int i = 0; i < l.Count; i++) if (l[i] != null) list.Add(l[i]);
		}
		catch { }
		return list;
	}

	// Cargo hold first, then what you carry.
	private static List<InventoryData> AmmoSources(HumanCharacterController pc, BasicVehicle v)
	{
		var list = new List<InventoryData>();
		try { if (v.vehicleData?.inventoryData != null) list.Add(v.vehicleData.inventoryData); } catch { }
		list.AddRange(Carried(pc));
		return list;
	}

	private static int Count(List<InventoryData> boxes, int id)
	{
		float n = 0f;
		foreach (InventoryData b in boxes)
		{
			try
			{
				var l = b.itemList;
				for (int i = 0; i < l.Count; i++) if (l[i] != null && l[i].itemId == id) n += Mathf.Max(1f, l[i].itemNumberFloat);
			}
			catch { }
		}
		return Mathf.FloorToInt(n + 0.001f);
	}

	// ------------------------------------------------------------------ geometry

	private static float VehAng(BasicVehicle v)
	{
		try { if (v.m_rigidbody != null) return v.m_rigidbody.rotation; } catch { }
		return v.transform.eulerAngles.z;
	}

	private static Vector2 VehPos(BasicVehicle v)
	{
		try { if (v.m_rigidbody != null) return v.m_rigidbody.position; } catch { }
		return v.transform.position;
	}

	private static float Heading(BasicVehicle v) => VehAng(v) + _fwd;

	// Which way the base car's nose points in its own space: while the local driver holds W
	// (and not S) the car rolls nose first, so its velocity shows where the nose is. S is the
	// mirror case. A few agreeing frames in a row settle it, and it is re-checked every drive.
	private static void FindForward(BasicVehicle v)
	{
		try
		{
			if (v == null || v != _mine) return;
			bool w = Input.GetKey(KeyCode.W), s = Input.GetKey(KeyCode.S);
			if (w == s) return;
			Vector2 vel = v.m_rigidbody != null ? v.m_rigidbody.velocity : Vector2.zero;
			if (vel.sqrMagnitude < 1.5f * 1.5f) { return; }
			float a = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg + (s ? 180f : 0f);
			float f = Mathf.Round(Mathf.DeltaAngle(VehAng(v), a) / 90f) * 90f;
			if (f <= -180f) f += 360f;
			if (Mathf.Abs(Mathf.DeltaAngle(f, _fwd)) < 1f) { _fwdVotes = 0; if (!_fwdKnown) Learned(); return; }
			if (++_fwdVotes < 15) return;
			_fwdVotes = 0;
			_fwd = f;
			Learned();
		}
		catch (Exception e) { TLog.Warn("forward: " + e.Message); }
	}

	private static void Learned()
	{
		_fwdKnown = true;
		_dirty = true;
		try { File.WriteAllText(FwdPath, $"v2|{BaseId()}|{_fwd.ToString("0", CI)}"); } catch { }
		TLog.Info($"Base car nose points {_fwd:0} degrees in its own space (learned from driving)");
	}

	private static Vector2 Dir(float deg) => new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));

	private static Vector2 Mouse()
	{
		Camera c = Camera.main;
		if (c == null) return Vector2.zero;
		Vector3 w = c.ScreenToWorldPoint(Input.mousePosition);
		return new Vector2(w.x, w.y);
	}

	private static bool MouseFree()
	{
		try { if (MouseController.IsMouseOverUI()) return false; } catch { }
		return Application.isFocused;
	}

	// ------------------------------------------------------------------ art

	private static Sprite[] LoadSheet(string name, int cell)
	{
		using Stream st = typeof(Tank).Assembly.GetManifestResourceStream("art." + name + ".png");
		if (st == null) return null;
		byte[] b = new byte[st.Length];
		int r;
		for (int i = 0; i < b.Length; i += r)
		{
			r = st.Read(b, i, b.Length - i);
			if (r <= 0) break;
		}
		var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
		tex.hideFlags = HideFlags.HideAndDontSave;
		ImageConversion.LoadImage(tex, (Il2CppStructArray<byte>)b);
		tex.filterMode = FilterMode.Point;
		int cols = tex.width / cell, rows = tex.height / cell;
		if (cols * rows < Frames) { TLog.Warn($"{name}: sheet is {tex.width}x{tex.height}, expected {Frames} frames of {cell}px"); return null; }
		var fr = new Sprite[Frames];
		for (int i = 0; i < Frames; i++)
		{
			int cx = i % cols, cy = i / cols;
			// texture rows start at the bottom, the sheet's first row is at the top
			var rect = new Rect(cx * cell, tex.height - (cy + 1) * cell, cell, cell);
			fr[i] = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), Ppu);
			fr[i].hideFlags = HideFlags.HideAndDontSave;
		}
		return fr;
	}

	private static bool Sprites()
	{
		if (!_spriteTried)
		{
			_spriteTried = true;
			try
			{
				_hullFr = LoadSheet("tank_hull32", HullCell);
				_turretFr = LoadSheet("tank_turret32", TurretCell);
				TLog.Info($"Art loaded: hull {_hullFr != null}, turret {_turretFr != null}");
			}
			catch (Exception e) { TLog.Warn("art: " + e.Message); }
		}
		return _hullFr != null && _turretFr != null;
	}

	private static int FrameOf(float deg)
	{
		int f = Mathf.RoundToInt(Mathf.Repeat(deg, 360f) / (360f / Frames));
		return f % Frames;
	}

	// Where a point of the 3D model (decimetres, model space at heading deg) lands on screen,
	// in world units from the tank's ground position.
	private static Vector2 Project(Vector3 m, float deg)
	{
		float a = deg * Mathf.Deg2Rad;
		float rx = m.x * Mathf.Cos(a) - m.y * Mathf.Sin(a);
		float ry = m.x * Mathf.Sin(a) + m.y * Mathf.Cos(a);
		float sy = ry * Mathf.Cos(Tilt) + m.z * Mathf.Sin(Tilt);
		return new Vector2(rx, sy) * (PxPerDm / Ppu * TankPlugin.TankScale.Value);
	}

	private static SpriteRenderer NewSr(string name, Sprite sp, Transform parent, int order)
	{
		var go = new GameObject(name);
		if (parent != null) go.transform.SetParent(parent, false);
		SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
		sr.sprite = sp;
		sr.sortingLayerID = _sortLayer;
		sr.sortingOrder = order;
		return sr;
	}

	private static Vis EnsureVis(BasicVehicle v)
	{
		if (_vis.TryGetValue(v.Pointer, out Vis x) && x.Root != null) return x;
		int o = TankPlugin.SortingOrder.Value;
		x = new Vis();
		x.Root = new GameObject("zt_tank");
		x.HullShadow = NewSr("hull_shadow", _hullFr[0], x.Root.transform, o - 1);
		x.HullShadow.color = new Color(0.03f, 0.04f, 0.02f, 0.35f);
		x.HullShadow.transform.localPosition = new Vector3(0.18f, -0.14f, 0f);
		x.Hull = NewSr("hull", _hullFr[0], x.Root.transform, o);
		x.Turret = NewSr("turret", _turretFr[0], x.Root.transform, o + 2);
		float s = TankPlugin.TankScale.Value;
		x.Root.transform.localScale = new Vector3(s, s, 1f);
		_vis[v.Pointer] = x;
		return x;
	}

	private static void ClearVis()
	{
		foreach (Vis x in _vis.Values)
		{
			try { if (x.Root != null) Object.Destroy(x.Root); } catch { }
		}
		_vis.Clear();
	}

	private static void HideCar(BasicVehicle v, Vis x, float now)
	{
		try { if (v.vehicleQuadRenderer != null && v.vehicleQuadRenderer.enabled) v.vehicleQuadRenderer.enabled = false; } catch { }
		try { if (v.vehicleMeshRenderer != null && v.vehicleMeshRenderer.enabled) v.vehicleMeshRenderer.enabled = false; } catch { }
		if (now < x.NextHide)
		{
			foreach (Renderer r in x.Hidden) if (r != null && r.enabled) r.enabled = false;
			return;
		}
		x.NextHide = now + 0.5f;
		x.Hidden.Clear();
		try
		{
			foreach (Renderer r in v.GetComponentsInChildren<Renderer>(true))
			{
				if (r == null || r.GetComponentInParent<BasicCharacterController>() != null) continue;
				// keep the headlight glow and tire marks out of it: only sprite/mesh bodies are hidden
				if (r.enabled) r.enabled = false;
				x.Hidden.Add(r);
			}
		}
		catch { }
	}

	// ------------------------------------------------------------------ per frame

	private static float _recipeCheck;
	private static bool _recipeLogged;

	// Battle Tank only: take the old Tank Kit's recipe out of the crafting lists (checked now and
	// then, in case the game registers mod recipes again).
	private static void HideClassicRecipe()
	{
		if (!TankPlugin.HideClassic.Value) return;
		float now = Time.realtimeSinceStartup;
		if (now < _recipeCheck) return;
		_recipeCheck = now + 5f;
		try
		{
			int id = Bundled.KitId();
			ItemManager im = ItemManager.instance;
			if (id <= 0 || im == null) return;
			var r = im.GetRecipeDatasById(id);
			if (r == null || r.Length == 0) return;
			im.UnregisterModRecipes(id);
			if (!_recipeLogged) { _recipeLogged = true; TLog.Info($"Battle Tank only: old Tank Kit recipe removed ({r.Length})"); }
		}
		catch (Exception e) { if (!_recipeLogged) { _recipeLogged = true; TLog.Warn("hiding the Tank Kit recipe: " + e.Message); } }
	}

	public static void Tick(float dt)
	{
		GameController gc = GameController.instance;
		if (gc == null) return;
		long sid;
		IntPtr gd = IntPtr.Zero;
		try
		{
			if (gc._gameData == null) return;
			sid = gc._gameData.createdDateTime;
			gd = gc._gameData.Pointer;
		}
		catch { return; }
		HumanCharacterController pc = gc.playerCharacter;
		if (pc == null) return;
		HideClassicRecipe();
		if (sid != _saveId || gd != _gdPtr)
		{
			_saveId = sid;
			_gdPtr = gd;
			Load();
		}
		if (!_sortSet)
		{
			try
			{
				SpriteRenderer sr = pc.GetComponentInChildren<SpriteRenderer>();
				if (sr != null) { _sortLayer = sr.sortingLayerID; _sortSet = true; }
			}
			catch { }
		}
		if (!Sprites()) return;
		LoadForward();
		float now = Time.realtimeSinceStartup;
		if (now >= _scanAt)
		{
			_scanAt = now + 0.5f;
			try { Track(); } catch (Exception e) { TLog.Warn("track: " + e.Message); _scanAt = now + 5f; }
		}
		CallKey(pc);

		BasicVehicle drv = null;
		try { drv = pc.drivingVehicle; } catch { }
		bool driving = drv != null && IsTank(drv) && SameDriver(drv, pc);
		_mine = driving ? drv : null;
		Status = null;
		if (!driving && drv == null) NearHint(pc);

		Draw(pc, dt, now);
		if (driving)
		{
			try { Gunner(pc, drv, now); } catch (Exception e) { TLog.Warn("gunner: " + e); }
		}
		TankFx.Tick();
		if (_dirty && now > _nextSave)
		{
			_nextSave = now + 5f;
			Save();
		}
	}

	private static bool SameDriver(BasicVehicle v, HumanCharacterController pc)
	{
		try { return v.driverCharacterController != null && v.driverCharacterController.Pointer == pc.Pointer; }
		catch { return false; }
	}

	private static void NearHint(HumanCharacterController pc)
	{
		BasicVehicle v = NearestTank(pc.transform.position, 5f);
		if (v == null) return;
		Status = $"Tank: fuel {Fuel(v):0.#} / {Cap(v.vehicleData):0} L  |  Shift+{TankPlugin.CallKey.Value} refuels from your gas cans";
	}

	private static void Draw(HumanCharacterController pc, float dt, float now)
	{
		var seen = new HashSet<IntPtr>();
		var live = BasicVehicle.s_liveVehicles;
		Vector2 mouse = Mouse();
		if (live != null)
		{
			for (int i = 0; i < live.Count; i++)
			{
				BasicVehicle v = live[i];
				if (!IsTank(v)) continue;
				seen.Add(v.Pointer);
				Vis x = EnsureVis(v);
				bool d3 = Is3D(v) && Model3D.Tick(v, x.TurretRel, now);
				if (Is3D(v)) TankBase.Apply(v);
				TankSound.Tick(v);
				if (x.Hull.enabled == d3) { x.Hull.enabled = !d3; x.HullShadow.enabled = !d3; x.Turret.enabled = !d3; }
				if (!d3)
				{
					FindForward(v);
					HideCar(v, x, now);
				}
				bool hidden = false;
				try { hidden = v.layerHiddenFlag || !v.gameObject.activeInHierarchy; } catch { }
				if (x.Root.activeSelf == hidden) x.Root.SetActive(!hidden);
				Vector2 p = VehPos(v);
				float h = Heading(v);
				if (d3 && Model3D.Heading(v, out float h3)) h = h3;
				x.Root.transform.position = new Vector3(p.x, p.y, v.transform.position.z - 0.5f);
				x.Root.transform.localScale = new Vector3(TankPlugin.TankScale.Value, TankPlugin.TankScale.Value, 1f);
				x.Heading = h;
				if (_mine != null && v.Pointer == _mine.Pointer)
				{
					// the turret turns toward the mouse at its traverse speed
					Vector2 piv = p + Project(RingTop, 0f);
					float want = Mathf.Atan2(mouse.y - piv.y, mouse.x - piv.x) * Mathf.Rad2Deg;
					float cur = h + x.TurretRel;
					float next = Mathf.MoveTowardsAngle(cur, want, TankPlugin.TurretSpeed.Value * dt);
					x.TurretRel = Mathf.DeltaAngle(0f, next - h);
				}
				x.TurretWorld = h + x.TurretRel;
				if (d3) continue;
				Sprite hs = _hullFr[FrameOf(h)];
				if (x.Hull.sprite != hs) { x.Hull.sprite = hs; x.HullShadow.sprite = hs; }
				Sprite ts = _turretFr[FrameOf(x.TurretWorld)];
				if (x.Turret.sprite != ts) x.Turret.sprite = ts;
				LogCamera(v);
			}
		}
		var drop = new List<IntPtr>();
		foreach (IntPtr k in _vis.Keys) if (!seen.Contains(k)) drop.Add(k);
		foreach (IntPtr k in drop)
		{
			try { if (_vis[k].Root != null) Object.Destroy(_vis[k].Root); } catch { }
			_vis.Remove(k);
			Model3D.Forget(k);
			TankSound.Forget(k);
		}
	}

	// Logs the game's car camera angle once, to match the art's tilt to it.
	private static bool _camLogged;

	private static void LogCamera(BasicVehicle v)
	{
		if (_camLogged) return;
		_camLogged = true;
		try
		{
			Camera c = v.vehicleCamera ?? v.vehicleCameraRef;
			if (c == null) { TLog.Info("Car camera: none on the base car"); return; }
			Transform t = c.transform;
			TLog.Info($"Car camera: euler {t.eulerAngles}, local euler {t.localEulerAngles}, local pos {t.localPosition}, ortho {c.orthographic} size {c.orthographicSize:0.##}, fov {c.fieldOfView:0.#}");
		}
		catch (Exception e) { TLog.Info("Car camera: " + e.Message); }
	}

	// ------------------------------------------------------------------ weapons

	private static void Gunner(HumanCharacterController pc, BasicVehicle v, float now)
	{
		if (!_vis.TryGetValue(v.Pointer, out Vis x)) return;
		var boxes = AmmoSources(pc, v);
		int rockets = Rockets.Find() && Rockets.AmmoId > 0 ? Count(boxes, Rockets.AmmoId) : 0;
		string rocketTxt = !Rockets.Find() ? "gun not ready" : Rockets.AmmoId <= 0 ? "105 mm shells not loaded" : (TankPlugin.RocketsNeedAmmo.Value ? rockets.ToString() : "unlimited");
		float reload = Mathf.Max(0f, _cannonAt - now);
		Status = $"Tank  |  LMB main gun (105 mm: {rocketTxt}{(reload > 0f ? $", loading {reload:0.0}s" : "")})  |  RMB machine gun  |  fuel {Fuel(v):0.#} / {Cap(v.vehicleData):0} L";
		if (!MouseFree()) return;
		if (Input.GetMouseButton(0) && now >= _cannonAt) FireCannon(pc, v, x, boxes, now);
		if (Input.GetMouseButton(1) && now >= _mgAt) FireMg(pc, v, x, boxes, now);
	}

	private static void FireCannon(HumanCharacterController pc, BasicVehicle v, Vis x, List<InventoryData> boxes, float now)
	{
		_cannonAt = now + Mathf.Max(0.2f, TankPlugin.ReloadSeconds.Value);
		if (!Rockets.Find())
		{
			Msg("The main gun isn't ready (see BepInEx\\Tank.log)");
			return;
		}
		if (TankPlugin.RocketsNeedAmmo.Value && Rockets.AmmoId <= 0)
		{
			Msg("The 105 mm shell item didn't load (see BepInEx\\Tank.log)");
			_cannonAt = now + 0.6f;
			return;
		}
		if (TankPlugin.RocketsNeedAmmo.Value && AutoIO.Take(boxes, Rockets.AmmoId, 1f) < 1f)
		{
			Msg($"No shells: put {Rockets.AmmoName} in the tank's cargo or your backpack");
			_cannonAt = now + 0.6f;
			return;
		}
		// the barrel points along the turret's actual angle; the rocket leaves from its drawn tip
		float ang = x.TurretWorld;
		Vector2 dir = Dir(ang);
		Vector2 pos = VehPos(v);
		Vector2 piv = pos + Project(RingTop, 0f);
		Vector2 muzzle = pos + Project(BarrelTip, ang) + dir * 0.3f;
		if (Is3D(v) && Model3D.TurretPoint(v, Model3D.BarrelTip, out Vector2 m3)) muzzle = m3 + dir * 0.3f;
		float dist = Mathf.Max((Mouse() - muzzle).magnitude, 3f);
		Vector2 target = muzzle + dir * dist;
		if (Rockets.Launch(pc, muzzle, target, ang))
		{
			TankFx.Blast(muzzle, ang, _sortLayer);
			TankAudio.Cannon(muzzle);
			try { GameController.instance.PlayerNoise(muzzle, TankPlugin.NoiseRadius.Value * 1.5f, false, true, pc.characterData.zLevel, null); } catch { }
		}
		else
		{
			Msg("The shell failed to fire (see BepInEx\\Tank.log)");
		}
	}

	// The roof gun works like the helicopter's door gun: it locks on to the hostile nearest
	// the mouse pointer and hits it directly, with a tracer.
	private static readonly int[] MgAmmo = { 102, 104 };

	private static void FireMg(HumanCharacterController pc, BasicVehicle v, Vis x, List<InventoryData> boxes, float now)
	{
		_mgAt = now + 1f / Mathf.Max(1f, TankPlugin.MgRate.Value);
		if (TankPlugin.MgNeedsAmmo.Value)
		{
			bool got = false;
			foreach (int id in MgAmmo) if (AutoIO.Take(boxes, id, 1f) >= 1f) { got = true; break; }
			if (!got)
			{
				Msg("No machine gun ammo: put 5.56mm or 7.62x39mm rounds in the cargo or your backpack");
				_mgAt = now + 1f;
				return;
			}
		}
		Vector2 muzzle = VehPos(v) + Project(MgTip, x.TurretWorld);
		if (Is3D(v) && Model3D.TurretPoint(v, Model3D.MgTip, out Vector2 m3)) muzzle = m3;
		Vector2 aim = Mouse();
		float ang = Mathf.Atan2(aim.y - muzzle.y, aim.x - muzzle.x) * Mathf.Rad2Deg;
		if ((aim - muzzle).magnitude > TankPlugin.MgRange.Value) aim = muzzle + (aim - muzzle).normalized * TankPlugin.MgRange.Value;
		Vector2 hitAt = aim;
		bool hit = false;
		try
		{
			BasicCharacterController best = null;
			CharacterBodyPart part = null;
			float bd = float.MaxValue;
			foreach (Collider2D c in Physics2D.OverlapCircleAll(aim, TankPlugin.MgAimRadius.Value))
			{
				if (c == null) continue;
				CharacterBodyPart bp = c.GetComponent<CharacterBodyPart>();
				BasicCharacterController ch = bp != null ? bp.character : c.GetComponentInParent<BasicCharacterController>();
				if (ch == null || ch.Pointer == pc.Pointer || ch.deadFlag || !Hostile(ch)) continue;
				float d = ((Vector2)ch.transform.position - aim).sqrMagnitude;
				if (d < bd) { bd = d; best = ch; part = bp; }
			}
			if (best != null && Random.value < 0.85f)
			{
				if (part == null) part = best.GetComponentInChildren<CharacterBodyPart>();
				hitAt = (Vector2)best.transform.position + Random.insideUnitCircle * 0.2f;
				best.OnHit(TankPlugin.MgDamage.Value, hitAt, (hitAt - muzzle).normalized * 4f, part, 1f, true, false, true, false);
				hit = true;
			}
			else
			{
				hitAt += Random.insideUnitCircle * TankPlugin.MgSpread.Value;
			}
		}
		catch (Exception e)
		{
			if (_hitLog++ < 5) TLog.Warn("mg hit: " + e.Message);
		}
		TankAudio.Mg(muzzle);
		try { GameController.instance.PlayerNoise(hitAt, TankPlugin.NoiseRadius.Value, false, true, pc.characterData.zLevel, null); } catch { }
		TankFx.Shot(muzzle, hitAt, hit, ang, 0.3f, _sortLayer);
	}

	internal static bool Hostile(BasicCharacterController ch)
	{
		try
		{
			if (BasicCharacterController.IsPlayerSideCharacter(ch)) return false;
			GOAPAgent ag = ch.GetComponent<GOAPAgent>();
			if (ag != null && ag.IsPlayerTeammate()) return false;
			if (ch.GetComponent<ZombieController>() != null) return true;
			bool camp = false;
			try { camp = BasicCharacterController.IsHostileCamp(ch.characterData); } catch { }
			bool human = ch.GetComponent<HumanCharacterController>() != null;
			return camp || !human || (ag != null && ag.isBanditAgent);
		}
		catch { return false; }
	}
}
