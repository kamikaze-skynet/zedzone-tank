using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZedTank;

/// <summary>
/// Turns the base pickup under a Battle Tank into a tank-sized vehicle of its own: a hit box the
/// size of the 3D tank, more weight, more armour and tank handling. Only the tagged tank
/// instances change; the game's pickups stay as they are.
/// </summary>
internal static class TankBase
{
	// width / length of the Desert Sentinel's hull including skirts
	private const float HullAspect = 3.97f / 8.14f;

	private class Orig
	{
		public Vector2 Size, Offset;
		public CapsuleDirection2D Dir;
		public float Mass, RbMass, HP, MaxSpeed, MaxTurn;
		public Vector2 SetSize;
		public float SetMass, SetMaxSpeed, SetMaxTurn;
		public bool Logged;
	}

	private static readonly Dictionary<IntPtr, Orig> _o = new Dictionary<IntPtr, Orig>();

	internal static void Apply(BasicVehicle v)
	{
		if (!TankPlugin.BaseEnabled.Value || v == null) return;
		try
		{
			if (!_o.TryGetValue(v.Pointer, out Orig o))
			{
				o = new Orig();
				CapsuleCollider2D c = v.vehicleCollider2D;
				if (c == null) return;
				o.Size = c.size; o.Offset = c.offset; o.Dir = c.direction;
				o.Mass = v.vehicleMass;
				o.RbMass = v.m_rigidbody != null ? v.m_rigidbody.mass : 0f;
				o.HP = v.vehicleBodyWorkHP;
				o.MaxSpeed = v.maxLinearSpeed;
				o.MaxTurn = v.maxAngularSpeed;
				// the long side of the capsule is the car's length
				float len = Mathf.Max(o.Size.x, o.Size.y), wid = Mathf.Min(o.Size.x, o.Size.y);
				float tl = len * TankPlugin.Model3DSize.Value;
				float tw = Mathf.Max(wid, tl * HullAspect);
				o.SetSize = o.Size.x >= o.Size.y ? new Vector2(tl, tw) : new Vector2(tw, tl);
				o.SetMass = o.Mass * TankPlugin.BaseMass.Value;
				o.SetMaxSpeed = o.MaxSpeed * TankPlugin.BaseTopSpeed.Value;
				o.SetMaxTurn = o.MaxTurn * TankPlugin.BaseTurn.Value;
				_o[v.Pointer] = o;
				// armour once, so damage taken so far isn't undone each frame
				if (TankPlugin.BaseArmour.Value > 1f && o.HP > 0f)
				{
					try { v.vehicleBodyWorkHP = o.HP * TankPlugin.BaseArmour.Value; } catch { }
				}
			}
			CapsuleCollider2D col = v.vehicleCollider2D;
			if (col != null && (col.size - o.SetSize).sqrMagnitude > 1e-6f) col.size = o.SetSize;
			// the game sizes a car's grass pressing, nav footprint and body checks from vehicleSize
			if ((v.vehicleSize - o.SetSize).sqrMagnitude > 1e-6f) v.vehicleSize = o.SetSize;
			if (Mathf.Abs(v.vehicleMass - o.SetMass) > 0.01f) v.vehicleMass = o.SetMass;
			if (v.m_rigidbody != null && o.RbMass > 0f)
			{
				float want = o.RbMass * TankPlugin.BaseMass.Value;
				if (Mathf.Abs(v.m_rigidbody.mass - want) > 0.01f) v.m_rigidbody.mass = want;
			}
			if (o.MaxSpeed > 0f && Mathf.Abs(v.maxLinearSpeed - o.SetMaxSpeed) > 0.001f) v.maxLinearSpeed = o.SetMaxSpeed;
			if (o.MaxTurn > 0f && Mathf.Abs(v.maxAngularSpeed - o.SetMaxTurn) > 0.001f) v.maxAngularSpeed = o.SetMaxTurn;
			PressGrass(v, col, o);
			BigPress(v, col, o);
			if (!o.Logged)
			{
				o.Logged = true;
				TLog.Info($"Tank base: hit box {o.Size} {o.Dir} -> {o.SetSize}, mass {o.Mass:0.#} -> {o.SetMass:0.#} (rigidbody {o.RbMass:0.#}), " +
					$"armour {o.HP:0} -> {v.vehicleBodyWorkHP:0}, top speed {o.MaxSpeed:0.##} -> {o.SetMaxSpeed:0.##}, turn {o.MaxTurn:0.##} -> {o.SetMaxTurn:0.##}, vehicleSize {v.vehicleSize}");
			}
		}
		catch (Exception e) { TLog.Warn("Tank base: " + e.Message); }
	}

	private static bool _grassWarned;
	private static readonly HashSet<Vector2Int> _tiles = new HashSet<Vector2Int>();
	private static readonly Dictionary<IntPtr, Vector3> _lastCrush = new Dictionary<IntPtr, Vector3>();

	// Tall grass is drawn over vehicles, and the game only presses it around a car's middle,
	// so it buried the big tank. A tank crushes the grass it drives over instead: every grass
	// tile under the hull is removed (for good only with CrushGrassPermanent).
	private static void PressGrass(BasicVehicle v, CapsuleCollider2D col, Orig o)
	{
		if (!TankPlugin.BasePressGrass.Value || col == null) return;
		try
		{
			Transform t = col.transform;
			Vector3 here = t.position;
			if (_lastCrush.TryGetValue(v.Pointer, out Vector3 last) && (last - here).sqrMagnitude < 0.04f) return;
			TerrainGrassController g = TerrainGrassController.instance;
			if (g == null) return;
			_lastCrush[v.Pointer] = here;
			bool alongX = o.SetSize.x >= o.SetSize.y;
			float len = alongX ? o.SetSize.x : o.SetSize.y, wid = alongX ? o.SetSize.y : o.SetSize.x;
			Vector2 la = alongX ? Vector2.right : Vector2.up, wa = alongX ? Vector2.up : Vector2.right;
			_tiles.Clear();
			const float step = 0.4f;
			for (float a = -len * 0.5f; a <= len * 0.5f + 1e-3f; a += step)
				for (float b = -wid * 0.5f; b <= wid * 0.5f + 1e-3f; b += step)
				{
					Vector3 w = t.TransformPoint((Vector3)(col.offset + la * a + wa * b));
					w.z = 0f;
					_tiles.Add(TerrainGrassController.WorldPositionToCoordinate(w));
				}
			int n = 0;
			foreach (Vector2Int c in _tiles)
				if (g.HasGrassAt(c))
				{
					if (TankPlugin.BaseGrassPermanent.Value) g.RemoveGrassPersistentAt(c, true);
					else g.RemoveGrassAt(c, true);
					n++;
				}
			if (n > 0) g.FlushPendingVertexUploads();
		}
		catch (Exception e) { if (!_grassWarned) { _grassWarned = true; TLog.Warn("Tank base grass: " + e.Message); } }
	}

	private static int _bigLog;

	// Like any other vehicle the tank pushes the grass down rather than cutting it, but over a
	// bigger area: one extra presser over the tank's middle, sized from the hull length.
	private static void BigPress(BasicVehicle v, CapsuleCollider2D col, Orig o)
	{
		float k = TankPlugin.BaseGrassPress.Value;
		if (k <= 0f || col == null) return;
		try
		{
			TerrainGrassController g = TerrainGrassController.instance;
			if (g == null) return;
			Transform t = col.transform;
			float len = Mathf.Max(o.SetSize.x, o.SetSize.y);
			Vector3 ls = t.lossyScale;
			float r = len * 0.5f * k * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y));
			Vector3 w = t.TransformPoint((Vector3)col.offset);
			w.z = 0f;
			int before = g.framePresserCount;
			bool ok = g.PressGrass(w, r, 1f);
			if (_bigLog < 3 || (!ok && _bigLog < 6))
			{
				_bigLog++;
				int cap = -1;
				try { cap = g.framePressers != null ? g.framePressers.Length : -1; } catch { }
				TLog.Info($"Tank grass press at {w} radius {r:0.##}: {(ok ? "ok" : "refused")}, pressers this frame {before} of {cap}");
			}
		}
		catch (Exception e) { if (!_grassWarned) { _grassWarned = true; TLog.Warn("Tank grass press: " + e.Message); } }
	}

	internal static void Forget(BasicVehicle v)
	{
		if (v != null) _o.Remove(v.Pointer);
	}
}
