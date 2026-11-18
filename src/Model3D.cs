using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZedTank;

// The Battle Tank is a real 3D model (blender/build_tank.py -> src/model3d/*.obj) drawn the same
// way the game draws its cars: the game films each car's 3D model with its own camera into a
// texture shown on a quad. We hide the base car's body meshes and put our hull and turret meshes
// in their place, so the game's camera, lighting and quad do the rest. The turret turns in 3D.
internal static class Model3D
{
	// model space (Unity axes, metres): +Z = front, +Y = up, +X = right. Origin = ground under the ring.
	private const float HullLength = 8.1f;
	private static readonly Vector3 TurretPivot = new Vector3(0f, 1.86f, 0f);
	internal static readonly Vector3 BarrelTip = new Vector3(0f, 0.25f, 5.75f);   // turret space
	internal static readonly Vector3 MgTip = new Vector3(0.46f, 1.15f, 1.05f);    // turret space (commander's .50 cal)

	private class Part
	{
		public Mesh Mesh;
		public List<string> Mats = new List<string>();
	}

	private static Part _hull, _turret;
	private static Dictionary<string, Color> _colors;
	private static Dictionary<string, string> _texFile;
	private static readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();
	private static bool _loadTried;

	private class State
	{
		public GameObject Pivot, Hull, Turret;
		public Camera Cam;
		public Transform Quad;
		public Vector3 QuadMin, QuadSize;
		public float BaseOrtho, SetOrtho;
		public Vector3 BaseQuadScale, SetQuad;
		public bool QuadSet;
		public float YawSign = -1f;
		public bool Failed;
		public float NextHide;
		public readonly List<Renderer> Hidden = new List<Renderer>();
		public readonly HashSet<IntPtr> Ours = new HashSet<IntPtr>();
	}

	private static readonly Dictionary<IntPtr, State> _st = new Dictionary<IntPtr, State>();
	private static int _log;

	// ------------------------------------------------------------------ loading the model

	private static string Res(string name)
	{
		var asm = typeof(Model3D).Assembly;
		using Stream s = asm.GetManifestResourceStream("model3d." + name);
		if (s != null)
		{
			using var r = new StreamReader(s);
			return r.ReadToEnd();
		}
		// big meshes ship gzipped (model3d/<name>.gz)
		using Stream g = asm.GetManifestResourceStream("model3d." + name + ".gz");
		if (g == null) return null;
		using var z = new System.IO.Compression.GZipStream(g, System.IO.Compression.CompressionMode.Decompress);
		using var rz = new StreamReader(z);
		return rz.ReadToEnd();
	}

	private static byte[] ResBytes(string name)
	{
		using Stream s = typeof(Model3D).Assembly.GetManifestResourceStream("model3d." + name);
		if (s == null) return null;
		byte[] b = new byte[s.Length];
		int r;
		for (int i = 0; i < b.Length; i += r)
		{
			r = s.Read(b, i, b.Length - i);
			if (r <= 0) break;
		}
		return b;
	}

	// the paint texture baked in Blender (map_Kd in the .mtl), embedded next to the .obj
	private static Texture2D Tex(string file)
	{
		if (string.IsNullOrEmpty(file)) return null;
		if (_tex.TryGetValue(file, out Texture2D t)) return t;
		t = null;
		try
		{
			byte[] b = ResBytes(file);
			if (b == null) TLog.Warn("3D model: missing texture " + file);
			else
			{
				t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
				t.hideFlags = HideFlags.HideAndDontSave;
				ImageConversion.LoadImage(t, (Il2CppStructArray<byte>)b);
				t.wrapMode = TextureWrapMode.Clamp;
				t.filterMode = FilterMode.Trilinear;
				t.anisoLevel = 4;
				t.Apply(true, false);
				Object.DontDestroyOnLoad(t);
				TLog.Info($"3D model texture '{file}': {t.width}x{t.height}");
			}
		}
		catch (Exception e) { TLog.Warn("3D model texture " + file + ": " + e.Message); t = null; }
		_tex[file] = t;
		return t;
	}

	private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

	private static void LoadMtl(string text)
	{
		if (text == null) return;
		string cur = null;
		foreach (string raw in text.Split('\n'))
		{
			string l = raw.Trim();
			if (l.StartsWith("newmtl ")) cur = l.Substring(7).Trim();
			else if (l.StartsWith("map_Kd ") && cur != null)
			{
				string f = l.Substring(7).Trim().Replace('\\', '/');
				int k = f.LastIndexOf('/');
				_texFile[cur] = k >= 0 ? f.Substring(k + 1) : f;
			}
			else if (l.StartsWith("Kd ") && cur != null)
			{
				string[] a = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				// Blender writes linear colours; materials take sRGB-ish values
				_colors[cur] = new Color(Mathf.LinearToGammaSpace(F(a[1])), Mathf.LinearToGammaSpace(F(a[2])), Mathf.LinearToGammaSpace(F(a[3])), 1f);
			}
		}
	}

	// Triangulated OBJ -> flat-shaded mesh with one submesh per material.
	// Blender axes (x front, y left, z up) become Unity axes (x right, y up, z front).
	private static Part LoadObj(string name)
	{
		string text = Res(name + ".obj");
		if (text == null) { TLog.Warn("3D model: missing resource " + name); return null; }
		LoadMtl(Res(name + ".mtl"));
		var pos = new List<Vector3>();
		var nrm = new List<Vector3>();
		var tc = new List<Vector2>();
		var norms = new List<Vector3>();
		var uvs = new List<Vector2>();
		var byMat = new Dictionary<string, List<int>>();
		var order = new List<string>();
		var verts = new List<Vector3>();
		string mat = "default";
		foreach (string raw in text.Split('\n'))
		{
			string l = raw.Trim();
			if (l.StartsWith("v "))
			{
				string[] a = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				float x = F(a[1]), y = F(a[2]), z = F(a[3]);
				pos.Add(new Vector3(-y, z, x));
			}
			else if (l.StartsWith("vn "))
			{
				string[] a = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				nrm.Add(new Vector3(-F(a[2]), F(a[3]), F(a[1])));
			}
			else if (l.StartsWith("vt "))
			{
				string[] a = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				tc.Add(new Vector2(F(a[1]), F(a[2])));
			}
			else if (l.StartsWith("usemtl ")) mat = l.Substring(7).Trim();
			else if (l.StartsWith("f "))
			{
				string[] a = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (!byMat.TryGetValue(mat, out var tris)) { tris = new List<int>(); byMat[mat] = tris; order.Add(mat); }
				var idx = new List<int>();
				var nid = new List<int>();
				var tid = new List<int>();
				for (int i = 1; i < a.Length; i++)
				{
					string[] parts = a[i].Split('/');
					int n = int.Parse(parts[0], CultureInfo.InvariantCulture);
					idx.Add(n > 0 ? n - 1 : pos.Count + n);
					int nn = parts.Length >= 3 && parts[2].Length > 0 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : 0;
					nid.Add(nn > 0 ? nn - 1 : (nn < 0 ? nrm.Count + nn : -1));
					int tt = parts.Length >= 2 && parts[1].Length > 0 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
					tid.Add(tt > 0 ? tt - 1 : (tt < 0 ? tc.Count + tt : -1));
				}
				for (int i = 1; i + 1 < idx.Count; i++)
				{
					// the axis swap mirrors the model, which also turns Blender's
					// counter-clockwise faces into Unity's clockwise front faces
					foreach (int c in new[] { 0, i, i + 1 })
					{
						tris.Add(verts.Count);
						verts.Add(pos[idx[c]]);
						norms.Add(nid[c] >= 0 && nid[c] < nrm.Count ? nrm[nid[c]] : Vector3.zero);
						uvs.Add(tid[c] >= 0 && tid[c] < tc.Count ? tc[tid[c]] : Vector2.zero);
					}
				}
			}
		}
		var m = new Mesh { name = "zt_" + name };
		m.indexFormat = IndexFormat.UInt32;
		m.vertices = verts.ToArray();
		m.subMeshCount = order.Count;
		for (int i = 0; i < order.Count; i++) m.SetTriangles(byMat[order[i]].ToArray(), i);
		// Blender's smooth-by-angle normals when the file has them, flat otherwise
		bool haveN = norms.Count == verts.Count && norms.TrueForAll(n => n.sqrMagnitude > 0.01f);
		if (haveN) m.normals = norms.ToArray();
		if (tc.Count > 0) m.uv = uvs.ToArray();
		else m.RecalculateNormals();
		m.RecalculateBounds();
		var p = new Part { Mesh = m };
		p.Mats.AddRange(order);
		Object.DontDestroyOnLoad(m);
		TLog.Info($"3D model '{name}': {verts.Count / 3} triangles, materials {string.Join(",", order)}, bounds {m.bounds.size}");
		return p;
	}

	private static bool Load()
	{
		if (_hull != null && _turret != null) return true;
		if (_loadTried) return false;
		_loadTried = true;
		try
		{
			_colors = new Dictionary<string, Color>();
			_texFile = new Dictionary<string, string>();
			_hull = LoadObj("tank_hull");
			_turret = LoadObj("tank_turret");
		}
		catch (Exception e) { TLog.Warn("3D model load: " + e); }
		return _hull != null && _turret != null;
	}

	// ------------------------------------------------------------------ attaching to a car

	private static Material BaseMaterial(BasicVehicle v)
	{
		Material b = null;
		try { b = v.originalBodySharedMat; } catch { }
		if (b == null) { try { b = v.vehicleMeshRenderer?.sharedMaterial; } catch { } }
		if (b == null)
		{
			try
			{
				foreach (MeshRenderer r in v.GetComponentsInChildren<MeshRenderer>(true))
				{
					if (r == null || r.sharedMaterial == null) continue;
					if (v.vehicleQuadRenderer != null && r.Pointer == v.vehicleQuadRenderer.Pointer) continue;
					b = r.sharedMaterial;
					break;
				}
			}
			catch { }
		}
		return b;
	}

	private static Material[] Materials(BasicVehicle v, Part p)
	{
		Material b = BaseMaterial(v);
		var list = new Material[p.Mats.Count];
		for (int i = 0; i < list.Length; i++)
		{
			Material m = b != null ? new Material(b) : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
			Texture2D tex = _texFile.TryGetValue(p.Mats[i], out string tf) ? Tex(tf) : null;
			Color c = tex != null ? Color.white : (_colors.TryGetValue(p.Mats[i], out Color cc) ? cc : Color.gray);
			foreach (string prop in new[] { "_BaseColor", "_Color", "_MainColor", "_Tint" })
				if (m.HasProperty(prop)) m.SetColor(prop, c);
			foreach (string prop in new[] { "_PaintTex", "_PaintLayerTex", "_DetailTex" })
				if (m.HasProperty(prop)) m.SetTexture(prop, Texture2D.whiteTexture);
			foreach (string prop in new[] { "_BaseMap", "_MainTex" })
				if (m.HasProperty(prop))
				{
					m.SetTexture(prop, tex != null ? tex : Texture2D.whiteTexture);
					m.SetTextureScale(prop, Vector2.one);
					m.SetTextureOffset(prop, Vector2.zero);
				}
			m.name = "zt_" + p.Mats[i];
			list[i] = m;
		}
		return list;
	}

	private static GameObject MakePart(string name, Part p, Transform parent, int layer, BasicVehicle v, State s)
	{
		var go = new GameObject(name);
		go.layer = layer;
		go.transform.SetParent(parent, false);
		go.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
		var mr = go.AddComponent<MeshRenderer>();
		mr.sharedMaterials = Materials(v, p);
		mr.shadowCastingMode = ShadowCastingMode.On;
		s.Ours.Add(mr.Pointer);
		return go;
	}

	private static string Path(Transform t)
	{
		string p = t.name;
		for (Transform a = t.parent; a != null && p.Length < 200; a = a.parent) p = a.name + "/" + p;
		return p;
	}

	private static State Setup(BasicVehicle v)
	{
		var s = new State();
		if (!Load()) { s.Failed = true; return s; }
		bool log = _log++ < 3;
		try
		{
			Transform parent = null;
			try { parent = v.vehicleModel != null ? v.vehicleModel.transform : null; } catch { }
			if (parent == null) { try { parent = v.vehicleBody != null ? v.vehicleBody.transform : null; } catch { } }
			if (parent == null) { try { parent = v.vehicleMeshRenderer?.transform.parent; } catch { } }
			if (parent == null) { TLog.Warn("3D tank: the base car has no model to attach to"); s.Failed = true; return s; }
			try { s.Cam = v.vehicleCamera ?? v.vehicleCameraRef; } catch { }
			try { s.Quad = v.vehicleQuadObj != null ? v.vehicleQuadObj.transform : v.vehicleQuadRenderer?.transform; } catch { }

			// the car's own frame, from its tyres: steering tyres are at the front
			var all = new List<Vector3>();
			var front = new List<Vector3>();
			try { foreach (Tire t in v.tireList) if (t != null) all.Add(parent.InverseTransformPoint(t.transform.position)); } catch { }
			try { foreach (Tire t in v.steeringTireList) if (t != null) front.Add(parent.InverseTransformPoint(t.transform.position)); } catch { }
			Vector3 c = Vector3.zero, f = Vector3.zero;
			foreach (Vector3 p in all) c += p;
			foreach (Vector3 p in front) f += p;
			Vector3 up = Vector3.up, fwd = Vector3.forward;
			float wheelbase = 0f;
			if (all.Count >= 3 && front.Count >= 1)
			{
				c /= all.Count; f /= front.Count;
				// plane normal of the tyre centres
				Vector3 n = Vector3.zero;
				for (int i = 0; i + 2 < all.Count; i++) n += Vector3.Cross(all[i + 1] - all[i], all[i + 2] - all[i]);
				if (n.sqrMagnitude < 1e-8f) n = Vector3.up;
				up = n.normalized;
				Vector3 camLocal = s.Cam != null ? parent.InverseTransformPoint(s.Cam.transform.position) - c : Vector3.up;
				if (Vector3.Dot(up, camLocal) < 0f) up = -up;
				fwd = Vector3.ProjectOnPlane(f - c, up).normalized;
				foreach (Vector3 p in all) wheelbase = Mathf.Max(wheelbase, Mathf.Abs(Vector3.Dot(p - c, fwd)) * 2f);
			}
			else
			{
				c = Vector3.zero;
				try
				{
					Bounds b = v.vehicleMeshRenderer.localBounds;
					c = parent.InverseTransformPoint(v.vehicleMeshRenderer.transform.TransformPoint(b.center));
					wheelbase = Mathf.Max(b.size.x, b.size.z) * 0.6f;
				}
				catch { wheelbase = 3f; }
			}
			if (TankPlugin.Model3DFlip.Value) fwd = -fwd;
			if (wheelbase < 0.1f) wheelbase = 3f;
			float carLen = wheelbase * 1.55f;
			float scale = carLen * TankPlugin.Model3DSize.Value / HullLength;
			int layer = 0;
			try { layer = v.vehicleMeshRenderer != null ? v.vehicleMeshRenderer.gameObject.layer : parent.gameObject.layer; } catch { }

			s.Pivot = new GameObject("zt_tank3d");
			s.Pivot.layer = layer;
			s.Pivot.transform.SetParent(parent, false);
			s.Pivot.transform.localPosition = c - up * (wheelbase * 0.12f);
			s.Pivot.transform.localRotation = Quaternion.LookRotation(fwd, up);
			s.Pivot.transform.localScale = Vector3.one * scale;
			s.Hull = MakePart("hull", _hull, s.Pivot.transform, layer, v, s);
			s.Turret = MakePart("turret", _turret, s.Pivot.transform, layer, v, s);
			s.Turret.transform.localPosition = TurretPivot;

			if (s.Quad != null)
			{
				Bounds qb = new Bounds(Vector3.zero, new Vector3(1f, 1f, 0f));
				try { var mf = s.Quad.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) qb = mf.sharedMesh.bounds; } catch { }
				s.QuadMin = qb.min; s.QuadSize = qb.size;
			}
			if (log)
			{
				string mat = "?";
				try { Material b = BaseMaterial(v); mat = b != null ? $"{b.name} ({b.shader?.name})" : "none"; } catch { }
				TLog.Info($"3D tank on '{Path(parent)}': tyres {all.Count} (front {front.Count}), wheelbase {wheelbase:0.##}, scale {scale:0.###}, up {up}, fwd {fwd}, layer {layer}, material {mat}, " +
					$"camera {(s.Cam != null ? $"'{s.Cam.name}' ortho {s.Cam.orthographicSize:0.##} at {parent.InverseTransformPoint(s.Cam.transform.position)}" : "none")}, quad {(s.Quad != null ? $"'{Path(s.Quad)}' scale {s.Quad.localScale} mesh {s.QuadMin}+{s.QuadSize}" : "none")}");
			}
		}
		catch (Exception e)
		{
			TLog.Warn("3D tank setup: " + e);
			s.Failed = true;
		}
		return s;
	}

	private static State Get(BasicVehicle v)
	{
		if (_st.TryGetValue(v.Pointer, out State s) && (s.Failed || s.Pivot != null)) return s;
		s = Setup(v);
		_st[v.Pointer] = s;
		return s;
	}

	// ------------------------------------------------------------------ per frame

	/// <summary>Keeps the 3D tank on the car: hides the car body, zooms the car camera out so the
	/// gun fits, turns the turret. Returns false if the 3D model couldn't be used.</summary>
	internal static bool Tick(BasicVehicle v, float turretRel, float now)
	{
		State s = Get(v);
		if (s.Failed) return false;
		if (s.Pivot == null) return false;
		HideBody(v, s, now);
		float k = Mathf.Max(1f, TankPlugin.Model3DZoom.Value);
		// Zoom the car camera out (and grow its quad by the same amount) so the long gun fits.
		// If the game changes either value itself, that becomes the new base.
		if (s.Cam != null)
		{
			float cur = s.Cam.orthographicSize;
			if (s.SetOrtho <= 0f || Mathf.Abs(cur - s.SetOrtho) > 0.0001f) { s.BaseOrtho = cur; s.SetOrtho = cur * k; s.Cam.orthographicSize = s.SetOrtho; }
		}
		if (s.Quad != null)
		{
			Vector3 cur = s.Quad.localScale;
			if (!s.QuadSet || (cur - s.SetQuad).sqrMagnitude > 1e-8f)
			{
				s.BaseQuadScale = cur;
				s.SetQuad = new Vector3(cur.x * k, cur.y * k, cur.z);
				s.Quad.localScale = s.SetQuad;
				s.QuadSet = true;
			}
		}
		// Which way a positive turn about the model's up axis shows on screen decides the sign.
		if (Map(s, s.Pivot.transform.TransformPoint(Vector3.zero), out Vector2 o)
			&& Map(s, s.Pivot.transform.TransformPoint(Vector3.forward), out Vector2 fz)
			&& Map(s, s.Pivot.transform.TransformPoint(Vector3.right), out Vector2 rx))
		{
			Vector2 a = fz - o, b = rx - o;
			float cross = a.x * b.y - a.y * b.x; // > 0: right is counter-clockwise of front, i.e. mirrored
			if (Mathf.Abs(cross) > 1e-6f) s.YawSign = cross < 0f ? -1f : 1f;
		}
		s.Turret.transform.localRotation = Quaternion.Euler(0f, s.YawSign * turretRel, 0f);
		return true;
	}

	/// <summary>The hull's heading on screen (degrees), from the 3D model itself.</summary>
	internal static bool Heading(BasicVehicle v, out float deg)
	{
		deg = 0f;
		if (!_st.TryGetValue(v.Pointer, out State s) || s.Failed || s.Pivot == null) return false;
		if (!Map(s, s.Pivot.transform.TransformPoint(Vector3.zero), out Vector2 o)) return false;
		if (!Map(s, s.Pivot.transform.TransformPoint(Vector3.forward * 4f), out Vector2 f)) return false;
		Vector2 d = f - o;
		if (d.sqrMagnitude < 1e-8f) return false;
		deg = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
		return true;
	}

	/// <summary>Where a point on the turret (turret space) appears in the world.</summary>
	internal static bool TurretPoint(BasicVehicle v, Vector3 local, out Vector2 world)
	{
		world = default;
		if (!_st.TryGetValue(v.Pointer, out State s) || s.Failed || s.Turret == null) return false;
		return Map(s, s.Turret.transform.TransformPoint(local), out world);
	}

	// A point of the 3D model -> where the game's quad shows it in the 2D world.
	private static bool Map(State s, Vector3 p, out Vector2 world)
	{
		world = default;
		if (s.Cam == null || s.Quad == null) return false;
		try
		{
			Vector3 vp = s.Cam.WorldToViewportPoint(p);
			Vector3 local = new Vector3(s.QuadMin.x + vp.x * s.QuadSize.x, s.QuadMin.y + vp.y * s.QuadSize.y, 0f);
			world = s.Quad.TransformPoint(local);
			return true;
		}
		catch { return false; }
	}

	private static void HideBody(BasicVehicle v, State s, float now)
	{
		if (now < s.NextHide)
		{
			foreach (Renderer r in s.Hidden) if (r != null && r.enabled) r.enabled = false;
			return;
		}
		s.NextHide = now + 0.5f;
		s.Hidden.Clear();
		IntPtr quad = IntPtr.Zero;
		try { if (v.vehicleQuadRenderer != null) quad = v.vehicleQuadRenderer.Pointer; } catch { }
		try
		{
			if (v.vehicleQuadRenderer != null && !v.vehicleQuadRenderer.enabled) v.vehicleQuadRenderer.enabled = true;
			foreach (Renderer r in v.GetComponentsInChildren<Renderer>(true))
			{
				if (r == null || r.Pointer == quad || s.Ours.Contains(r.Pointer)) continue;
				if (r.GetComponentInParent<BasicCharacterController>() != null) continue;
				if (r.TryCast<MeshRenderer>() == null && r.TryCast<SkinnedMeshRenderer>() == null) continue; // keep lights, trails, sprites
				if (r.enabled) r.enabled = false;
				s.Hidden.Add(r);
			}
		}
		catch { }
	}

	internal static void Forget(IntPtr v)
	{
		if (_st.TryGetValue(v, out State s))
		{
			try { if (s.Pivot != null) Object.Destroy(s.Pivot); } catch { }
			_st.Remove(v);
		}
	}
}
