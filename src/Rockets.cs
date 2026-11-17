using System;
using System.Text;
using UnityEngine;

namespace ZedTank;

// The main gun fires the game's own rocket: the recoilless rifle round (RecoillessRound).
// The launcher is found among the game's guns by its bullet prefab, so its ammo item,
// speed and range come from the game data.
internal static class Rockets
{
	private static GameObject _prefab;
	private static float _retryAt;
	private static bool _logged;
	private static int _launchLog;
	internal static int AmmoId = -1;
	internal static int LauncherId = -1;
	internal static string AmmoName = "rockets";
	internal static string FireClipName;
	private static float _speed = 40f;

	internal static bool Find()
	{
		if (_prefab != null) return true;
		float now = Time.realtimeSinceStartup;
		if (now < _retryAt) return false;
		_retryAt = now + 10f;
		ItemManager im = ItemManager.instance;
		if (im == null || im.itemList == null) return false;
		var log = new StringBuilder();
		try
		{
			var list = im.itemList;
			for (int i = 0; i < list.Count && _prefab == null; i++)
			{
				ItemAttr_RangedWeapon w = list[i]?.TryCast<ItemAttr_RangedWeapon>();
				if (w == null) continue;
				string bp = null;
				try { bp = w.bulletPrefabName; } catch { }
				if (string.IsNullOrEmpty(bp)) continue;
				GameObject go = null;
				try { go = DeployableItem_Turret.LoadBulletPrefab(bp); } catch { }
				if (go == null) { try { go = Resources.Load<GameObject>(bp); } catch { } }
				if (go == null) continue;
				RecoillessRound rr = go.GetComponent<RecoillessRound>();
				if (rr == null) { try { rr = go.GetComponentInChildren<RecoillessRound>(true); } catch { } }
				if (rr == null) continue;
				_prefab = go;
				AmmoId = w.ammoId;
				LauncherId = w.itemId;
				if (w.bulletSpeed > 1f) _speed = w.bulletSpeed;
				try { FireClipName = w.fireClip; } catch { }
				try { AmmoName = im.FindItemAttrById(AmmoId)?.ItemName_EN ?? AmmoName; } catch { }
				string wn = "?";
				try { wn = w.ItemName_EN; } catch { }
				log.Append($"launcher {LauncherId} '{wn}', bullet prefab '{bp}', ammo {AmmoId} '{AmmoName}', speed {w.bulletSpeed}, range {w.fireRange}, clip '{FireClipName}'");
			}
		}
		catch (Exception e) { log.Append("weapon scan failed: " + e.Message + "; "); }
		if (_prefab == null)
		{
			try
			{
				foreach (RecoillessRound rr in Resources.FindObjectsOfTypeAll<RecoillessRound>())
				{
					if (rr == null || rr.gameObject.scene.IsValid()) continue;
					_prefab = rr.gameObject;
					log.Append($"fallback prefab '{rr.gameObject.name}' (ammo unknown)");
					break;
				}
			}
			catch { }
		}
		if (_prefab != null || !_logged)
		{
			_logged = true;
			TLog.Info("Rockets: " + (log.Length > 0 ? log.ToString() : "no recoilless launcher found yet"));
		}
		return _prefab != null;
	}

	internal static bool Launch(HumanCharacterController pc, Vector2 muzzle, Vector2 target, float angle)
	{
		if (!Find()) return false;
		try
		{
			GameObject go = Object.Instantiate(_prefab, new Vector3(muzzle.x, muzzle.y, pc.transform.position.z), Quaternion.Euler(0f, 0f, angle));
			go.SetActive(true);
			RecoillessRound rr = go.GetComponent<RecoillessRound>() ?? go.GetComponentInChildren<RecoillessRound>(true);
			if (rr == null)
			{
				Object.Destroy(go);
				return false;
			}
			try { rr.throwerCharacter = pc; } catch { }
			try { rr.sourceItemId = LauncherId; } catch { }
			try { rr.throwerStandVz = pc.characterData.zLevel; } catch { }
			// A tank gun has no back blast (it would hit the tank's own crew).
			try { rr.backBlastDamage = 0f; rr.backBlastForce = 0f; rr.backBlastRange = 0f; rr.backBlastSmokeCount = 0; } catch { }
			try { rr.directHitDamage *= Mathf.Max(0f, TankPlugin.RocketDamage.Value); } catch { }
			float speed = TankPlugin.RocketSpeed.Value > 0f ? TankPlugin.RocketSpeed.Value : _speed;
			rr.Init(muzzle, target, speed);
			if (_launchLog++ < 5)
			{
				string mask = "?";
				try { mask = rr.layerMask.value.ToString(); } catch { }
				TLog.Info($"Rocket away: {muzzle} -> {target} at {speed:0.#}, layerMask {mask}, direct hit {rr.directHitDamage:0}, range {rr.maxFlightRange:0.#}");
			}
			return true;
		}
		catch (Exception e)
		{
			TLog.Warn("rocket launch: " + e);
			return false;
		}
	}
}
