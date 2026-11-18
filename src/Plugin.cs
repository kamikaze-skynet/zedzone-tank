using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace ZedTank;

[BepInPlugin(Guid, "Tank", Version)]
public class TankPlugin : BasePlugin
{
	public const string Guid = "girthy_mods.zedzone.tank";
	public const string Version = "1.5.6";

	internal static ManualLogSource L;

	internal static ConfigEntry<bool> Enabled;
	internal static ConfigEntry<KeyCode> CallKey;
	internal static ConfigEntry<int> MaxTanks;
	internal static ConfigEntry<float> NearbyRadius;
	internal static ConfigEntry<float> StartFuel;
	internal static ConfigEntry<float> TankScale;
	internal static ConfigEntry<int> SortingOrder;
	internal static ConfigEntry<float> CrewDamage;
	internal static ConfigEntry<float> TurretSpeed;
	internal static ConfigEntry<float> ReloadSeconds;
	internal static ConfigEntry<bool> RocketsNeedAmmo;
	internal static ConfigEntry<bool> RocketsIfNoShells;
	internal static ConfigEntry<bool> RequireKit;
	internal static ConfigEntry<bool> CrewSafe;
	internal static ConfigEntry<float> Model3DSize;
	internal static ConfigEntry<float> Model3DZoom;
	internal static ConfigEntry<bool> DrawDiagnostics;
	internal static ConfigEntry<bool> BaseEnabled, BasePressGrass, BaseGrassPermanent;
	internal static ConfigEntry<float> BaseMass, BaseArmour, BaseTopSpeed, BaseTurn, BaseGrassPress, BaseGrassSouth;
	internal static ConfigEntry<int> Model3DPictureSize;
	internal static ConfigEntry<float> Model3DNearClip;
	internal static ConfigEntry<bool> Model3DFlip;
	internal static ConfigEntry<string> MgSoundWeapon;
	internal static ConfigEntry<float> CrewSafeRadius;
	internal static ConfigEntry<float> RocketDamage;
	internal static ConfigEntry<float> RocketSpeed;
	internal static ConfigEntry<bool> MgNeedsAmmo;
	internal static ConfigEntry<float> MgDamage;
	internal static ConfigEntry<float> MgRate;
	internal static ConfigEntry<float> MgAimRadius;
	internal static ConfigEntry<float> MgSpread;
	internal static ConfigEntry<float> MgRange;
	internal static ConfigEntry<float> NoiseRadius;
	internal static ConfigEntry<float> FxScale;
	internal static ConfigEntry<float> Volume;

	public override void Load()
	{
		L = Log;
		Enabled = Config.Bind("General", "Enabled", true, "Turn the tank mod on or off.");
		CallKey = Config.Bind("General", "CallKey", KeyCode.PageUp, "Press to call a tank in front of you. Shift + this key next to a tank pours your gas cans into it.");
		MaxTanks = Config.Bind("General", "MaxTanks", 1, new ConfigDescription("How many tanks each save can call in with the key. 0 = no limit. (Spawn Menu's Tank kit ignores this.)", new AcceptableValueRange<int>(0, 20)));
		NearbyRadius = Config.Bind("General", "NearbyRadius", 30f, new ConfigDescription("You can't call another tank while one is parked this close (tiles).", new AcceptableValueRange<float>(0f, 500f)));
		StartFuel = Config.Bind("General", "StartFuel", 0.5f, new ConfigDescription("How full a new tank's fuel tank is (0 to 1).", new AcceptableValueRange<float>(0f, 1f)));
		TankScale = Config.Bind("Look", "Scale", 1.1f, new ConfigDescription("Size of the tank's art.", new AcceptableValueRange<float>(0.5f, 3f)));
		Model3DSize = Config.Bind("BattleTank", "Size", 1.25f, "Battle Tank (3D model) hull length compared with the base car's length.");
		Model3DZoom = Config.Bind("BattleTank", "CameraZoomOut", 2.2f, "How far the car camera zooms out for the Battle Tank so its long gun isn't cut off (the picture is enlarged by the same amount, so the tank keeps its size).");
		Model3DPictureSize = Config.Bind("BattleTank", "PictureSize", 768, "Pixel size of the picture the car camera films the Battle Tank into (the game uses 256 for cars; the tank is zoomed out, so it needs more to stay sharp). 0 keeps the game's picture.");
		Model3DNearClip = Config.Bind("BattleTank", "CameraNearClip", 12f, "How far in front of the car camera the Battle Tank may reach before it is cut off (the camera is orthographic, so this can reach behind it).");
		DrawDiagnostics = Config.Bind("Debug", "DrawDiagnostics", true, "Write how the Battle Tank is drawn (car camera, its picture, the quad on the map, every renderer) to Tank.log a few times after it appears.");
		BaseEnabled = Config.Bind("BattleTankBase", "Enabled", true, "Make the pickup under a Battle Tank into a tank of its own: hit box the size of the tank, heavier, more armour, tank handling.");
		BaseMass = Config.Bind("BattleTankBase", "Weight", 4f, "Battle Tank weight compared with the pickup it is built on (heavier pushes cars aside and isn't shoved by them).");
		BaseArmour = Config.Bind("BattleTankBase", "Armour", 5f, "Battle Tank bodywork health compared with the pickup.");
		BaseTopSpeed = Config.Bind("BattleTankBase", "TopSpeed", 0.8f, "Battle Tank top speed compared with the pickup.");
		BaseTurn = Config.Bind("BattleTankBase", "TurnRate", 1f, "Battle Tank turning speed compared with the pickup.");
		BaseGrassPress = Config.Bind("BattleTankBase", "GrassPushRadius", 2f, "How far around the Battle Tank grass is pushed down: a circle whose radius is this times half the hull length (1.1 just covers the hull; 2 covers the whole tank with a wide margin).");
		BaseGrassSouth = Config.Bind("BattleTankBase", "GrassPushDown", 0.6f, "A second grass-push circle this far below the tank (times the hull length), because grass below the tank grows up the screen over it. 0 = off.");
		BasePressGrass = Config.Bind("BattleTankBase", "TankCrushesGrass", false, "The Battle Tank crushes the grass it drives over so tall grass doesn't cover it.");
		BaseGrassPermanent = Config.Bind("BattleTankBase", "CrushGrassPermanent", false, "Crushed grass stays gone in the world save (tank trails). Off: it grows back after a reload.");
		Model3DFlip = Config.Bind("BattleTank", "FlipModel", false, "Turn the 3D model around if it drives backwards.");
		SortingOrder = Config.Bind("Look", "SortingOrder", 32000, "Draw order of the tank's art (higher draws on top of more things).");
		CrewDamage = Config.Bind("Armor", "CrewDamageTaken", 0.1f, new ConfigDescription("Share of damage that reaches the crew inside (a car is much higher).", new AcceptableValueRange<float>(0f, 1f)));
		TurretSpeed = Config.Bind("MainGun", "TurretTurnSpeed", 120f, "How fast the turret turns toward the mouse (degrees per second).");
		ReloadSeconds = Config.Bind("MainGun", "ReloadSeconds", 2.5f, "Seconds between main gun shots.");
		RocketsNeedAmmo = Config.Bind("MainGun", "UseAmmo", true, "Each shot uses one 105 mm Tank Shell from the tank's cargo or your backpack.");
		RequireKit = Config.Bind("General", "RequireTankKit", true, "Page Up only calls in a tank when you carry a Tank Kit (built at a workbench), and uses the kit up. Off = free call-ins, limited by MaxTanks.");
		CrewSafe = Config.Bind("Armor", "CrewSafeFromOwnShells", true, "The tank's own shells can't hurt anyone inside a tank (driver and riders).");
		CrewSafeRadius = Config.Bind("Armor", "CrewSafeRadius", 3f, "Anyone this close to a tank's centre counts as riding in it (tiles).");
		RocketsIfNoShells = Config.Bind("MainGun", "RocketsIfNoShells", false, "If the 105 mm shell item fails to load, use the game's recoilless rockets as ammo instead.");
		RocketDamage = Config.Bind("MainGun", "DirectHitDamageMultiplier", 1f, "Multiplies the rocket's direct hit damage (its blast is the game's own).");
		RocketSpeed = Config.Bind("MainGun", "RocketSpeed", 0f, "Rocket speed. 0 = the game's recoilless launcher speed.");
		MgNeedsAmmo = Config.Bind("MachineGun", "UseAmmo", true, "Each round uses one 5.56mm or 7.62x39mm round from the cargo or your backpack.");
		MgDamage = Config.Bind("MachineGun", "Damage", 400f, "Damage per round (it locks on to the hostile nearest the mouse; a normal zombie has about 1100 HP).");
		MgRate = Config.Bind("MachineGun", "RoundsPerSecond", 9f, "Machine gun fire rate.");
		MgAimRadius = Config.Bind("MachineGun", "AimRadius", 1.6f, "Lock-on range around the mouse pointer (tiles).");
		MgSpread = Config.Bind("MachineGun", "Spread", 0.6f, "Spread when nothing is locked on (tiles).");
		MgRange = Config.Bind("MachineGun", "Range", 22f, "Machine gun range (tiles).");
		NoiseRadius = Config.Bind("Effects", "NoiseRadius", 30f, "Zombies within this range hear the guns.");
		FxScale = Config.Bind("Effects", "EffectScale", 2f, "Size of tracers, muzzle flashes and smoke.");
		MgSoundWeapon = Config.Bind("Effects", "MachineGunSoundFrom", "", "Part of a gun's name (or fire sound name) to borrow the machine gun sound from, e.g. M249. Empty = pick a machine gun automatically. The choices are listed in BepInEx\\Tank.log.");
		Volume = Config.Bind("Effects", "Volume", 0.8f, new ConfigDescription("Gun volume.", new AcceptableValueRange<float>(0f, 1f)));
		try
		{
			var lh = new Harmony(Guid + ".items");
			var loadAll = AccessTools.Method(typeof(ModLoader), nameof(ModLoader.LoadAll));
			if (loadAll != null) lh.Patch(loadAll, postfix: new HarmonyMethod(typeof(TankPlugin), nameof(LoadAllPost)));
		}
		catch (Exception e) { Log.LogWarning("ModLoader.LoadAll hook failed: " + e.Message); }
		AddComponent<TankRunner>();
		Log.LogInfo($"Tank {Version} loaded. Call key: {CallKey.Value}.");
	}

	private static bool _patched;

	internal static void EnsurePatched()
	{
		if (_patched) return;
		_patched = true;
		var h = new Harmony(Guid);
		Type[] types = { typeof(P_MouseDown), typeof(P_MouseHold), typeof(P_Name1), typeof(P_Name2), typeof(P_Name3) };
		int ok = 0;
		foreach (Type t in types)
		{
			try { h.PatchAll(t); ok++; }
			catch (Exception e) { TLog.Warn("patch " + t.Name + " failed: " + e.Message); }
		}
		int saves = 0;
		foreach (string name in new[] { "SaveGameData", "SaveGameDataAsync" })
		{
			foreach (MethodInfo m in AccessTools.GetDeclaredMethods(typeof(GameDataManager)))
			{
				if (m.Name != name) continue;
				try { h.Patch(m, new HarmonyMethod(typeof(TankPlugin), nameof(OnSave))); saves++; }
				catch (Exception e) { TLog.Warn("save hook " + name + " failed: " + e.Message); }
			}
		}
		TLog.Info($"Patches: {ok}/{types.Length} ok, save hooks {saves}.");
		Safety.Patch(h);
	}

	private static void LoadAllPost()
	{
		try { Bundled.LoadMissing("after game mods"); } catch (Exception e) { TLog.Warn("bundled items: " + e.Message); }
	}

	private static void OnSave()
	{
		try { Tank.OnGameSave(); } catch { }
	}
}

public class TankRunner : MonoBehaviour
{
	internal static bool Active;
	private GUIStyle _style;
	private Texture2D _white;

	public TankRunner(IntPtr ptr) : base(ptr) { }

	private void Update()
	{
		TankPlugin.EnsurePatched();
		if (Time.frameCount % 120 == 0) { try { var im = ItemManager.instance; if (im != null && im.itemList != null && im.itemList.Count > 0) Bundled.LoadMissing("late"); } catch { } }
		Active = TankPlugin.Enabled.Value;
		if (!Active) return;
		try { Tank.Tick(Time.deltaTime); }
		catch (Exception e) { TLog.Warn("tick: " + e.Message); }
	}

	private void OnGUI()
	{
		try
		{
			if (!Active || Event.current == null || Event.current.type != EventType.Repaint) return;
			string a = Tank.Message();
			string b = Tank.Status;
			if (!string.IsNullOrEmpty(b)) Hint(b, 0);
			if (!string.IsNullOrEmpty(a)) Hint(a, 1);
		}
		catch { }
	}

	private void Hint(string text, int row)
	{
		if (_style == null)
		{
			_style = new GUIStyle(GUI.skin.label);
			_style.alignment = TextAnchor.MiddleCenter;
			_style.fontSize = 15;
			_style.normal.textColor = Color.white;
			_white = new Texture2D(1, 1);
			_white.SetPixel(0, 0, Color.white);
			_white.Apply();
			_white.hideFlags = HideFlags.HideAndDontSave;
		}
		float s = Mathf.Clamp(Screen.height / 1080f, 0.75f, 2f);
		Matrix4x4 old = GUI.matrix;
		GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
		float sw = Screen.width / s, sh = Screen.height / s;
		float w = Mathf.Min(Mathf.Max(460f, _style.CalcSize(new GUIContent(text)).x + 40f), sw - 40f);
		float h = 34f;
		float x = (sw - w) / 2f;
		float y = sh * 0.72f - 42f - row * (h + 6f);
		Color c = GUI.color;
		GUI.color = new Color(0.07f, 0.07f, 0.08f, 0.85f);
		GUI.DrawTexture(new Rect(x, y, w, h), _white);
		GUI.color = new Color(0.45f, 0.55f, 0.25f, 1f);
		GUI.DrawTexture(new Rect(x, y, 3f, h), _white);
		GUI.color = c;
		GUI.Label(new Rect(x, y, w, h), text, _style);
		GUI.matrix = old;
	}
}
