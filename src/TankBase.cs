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
			if (Mathf.Abs(v.vehicleMass - o.SetMass) > 0.01f) v.vehicleMass = o.SetMass;
			if (v.m_rigidbody != null && o.RbMass > 0f)
			{
				float want = o.RbMass * TankPlugin.BaseMass.Value;
				if (Mathf.Abs(v.m_rigidbody.mass - want) > 0.01f) v.m_rigidbody.mass = want;
			}
			if (o.MaxSpeed > 0f && Mathf.Abs(v.maxLinearSpeed - o.SetMaxSpeed) > 0.001f) v.maxLinearSpeed = o.SetMaxSpeed;
			if (o.MaxTurn > 0f && Mathf.Abs(v.maxAngularSpeed - o.SetMaxTurn) > 0.001f) v.maxAngularSpeed = o.SetMaxTurn;
			PressGrass(v, col, o);
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
	private static int _pressFails;

	// The game flattens grass around a car's middle only, so tall grass stood over most of the
	// big tank. Press it along the whole hull every frame.
	private static void PressGrass(BasicVehicle v, CapsuleCollider2D col, Orig o)
	{
		if (!TankPlugin.BasePressGrass.Value || col == null) return;
		try
		{
			TerrainGrassController g = TerrainGrassController.instance;
			if (g == null) return;
			Transform t = col.transform;
			bool alongX = o.SetSize.x >= o.SetSize.y;
			float len = alongX ? o.SetSize.x : o.SetSize.y, wid = alongX ? o.SetSize.y : o.SetSize.x;
			Vector2 axis = alongX ? Vector2.right : Vector2.up;
			Vector3 ls = t.lossyScale;
			float r = wid * 0.62f * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y));
			const int n = 5;
			for (int i = 0; i < n; i++)
			{
				float f = (i / (float)(n - 1) - 0.5f) * (len - wid * 0.5f);
				Vector3 w = t.TransformPoint((Vector3)(col.offset + axis * f));
				w.z = 0f;
				if (!g.PressGrass(w, r, 1f) && ++_pressFails == 50) TLog.Info("Tank base: the game's grass pressers are full, some grass may stay up");
			}
		}
		catch (Exception e) { if (!_grassWarned) { _grassWarned = true; TLog.Warn("Tank base grass: " + e.Message); } }
	}

	internal static void Forget(BasicVehicle v)
	{
		if (v != null) _o.Remove(v.Pointer);
	}
}
