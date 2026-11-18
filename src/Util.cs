using System.Collections.Generic;
using Il2CppSystem.Collections.Generic;
using UnityEngine;

namespace ZedTank;

// Fuel and inventory helpers (FuelGen / AutoIO).
internal static class FuelGen
{
	private static System.Collections.Generic.HashSet<int> _fuelIds;

	internal static System.Collections.Generic.HashSet<int> FuelIds()
	{
		if (_fuelIds != null && _fuelIds.Count > 0)
		{
			return _fuelIds;
		}
		var set = new System.Collections.Generic.HashSet<int>();
		try
		{
			int id = VehicleFuelTransfer.fuelLiquidItemId;
			if (id > 0) set.Add(id);
		}
		catch { }
		try
		{
			ItemManager im = ItemManager.instance;
			if (im == null) return set;
			string[] words = { "汽油", "柴油", "燃油", "gasoline", "diesel", "petrol", "fuel" };
			var e = im.itemAttrDic.GetEnumerator();
			while (e.MoveNext())
			{
				ItemAttr a = e.Current.Value;
				if (a == null || (int)a.itemType != 14) continue;
				string t = (a.ItemName + "|" + a.ItemName_EN).ToLowerInvariant();
				foreach (string w in words)
				{
					if (t.Contains(w)) { set.Add(a.itemId); break; }
				}
			}
			if (set.Count > 0)
			{
				_fuelIds = set;
				TLog.Info("FUEL ids: " + string.Join(",", set));
			}
		}
		catch (System.Exception ex)
		{
			TLog.Info("fuel ids: " + ex.Message);
		}
		return set;
	}
}

internal static class AutoIO
{
	public static float Take(System.Collections.Generic.List<InventoryData> boxes, int id, float n)
	{
		float got = 0f;
		foreach (InventoryData box in boxes)
		{
			var list = box.itemList;
			for (int i = list.Count - 1; i >= 0 && got < n - 0.0001f; i--)
			{
				ItemData it = list[i];
				if (it == null || it.itemId != id) continue;
				float have = Mathf.Max(1f, it.itemNumberFloat);
				float take = Mathf.Min(have, n - got);
				if (have - take <= 0.0001f)
				{
					try { box.RemoveItem(it, true); } catch { continue; }
				}
				else
				{
					it.itemNumberFloat = have - take;
					try { box.RefreshTotalItemWeight(); } catch { }
				}
				got += take;
			}
			if (got >= n - 0.0001f) break;
		}
		return got;
	}
}
