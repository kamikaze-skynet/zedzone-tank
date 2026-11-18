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
		public RenderTexture Pic;
		public float NearSet = float.NaN;
		public float NextDiag = -1f;
		public int Diags;
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
		if (!haveN) m.RecalculateNormals();
		{
			// solid white vertex colours, in case the shader reads them as tint or alpha
			var col = new Color32[verts.Count];
			for (int i = 0; i < col.Length; i++) col[i] = new Color32(255, 255, 255, 255);
			m.colors32 = col;
		}
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

	private static readonly Dictionary<int, Texture2D> _solid = new Dictionary<int, Texture2D>();
	private static bool _propsLogged;

	// fully transparent texture: an empty slot reads as white in a shader graph,
	// which painted a white spray layer over the whole tank
	private static Texture2D _clear;
	private static Texture2D Clear()
	{
		if (_clear != null) return _clear;
		_clear = new Texture2D(4, 4, TextureFormat.RGBA32, false);
		_clear.hideFlags = HideFlags.HideAndDontSave;
		for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) _clear.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
		_clear.Apply(false, false);
		Object.DontDestroyOnLoad(_clear);
		return _clear;
	}

	// one-pixel-colour texture for parts that have no baked paint
	private static Texture2D Solid(Color c)
	{
		Color32 k = c;
		int key = (k.r << 16) | (k.g << 8) | k.b;
		if (_solid.TryGetValue(key, out Texture2D t) && t != null) return t;
		t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
		t.hideFlags = HideFlags.HideAndDontSave;
		for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) t.SetPixel(x, y, new Color(c.r, c.g, c.b, 1f));
		t.wrapMode = TextureWrapMode.Clamp;
		t.Apply(false, false);
		Object.DontDestroyOnLoad(t);
		_solid[key] = t;
		return t;
	}

	private static Material[] Materials(BasicVehicle v, Part p)
	{
		Material b = BaseMaterial(v);
		var list = new Material[p.Mats.Count];
		for (int i = 0; i < list.Length; i++)
		{
			Material m = b != null ? new Material(b) : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
			Texture2D tex = _texFile.TryGetValue(p.Mats[i], out string tf) ? Tex(tf) : null;
			Color c = _colors.TryGetValue(p.Mats[i], out Color cc) ? cc : Color.gray;
			// the car shader (VehicleUnlit) samples the car's own colour atlas with the
			// mesh UVs, so every texture slot gets our paint (or a solid colour) and
			// every colour slot goes white, whatever the shader calls them
			Texture2D paint = tex != null ? tex : Solid(c);
			var log = new System.Text.StringBuilder();
			try
			{
				Shader sh = m.shader;
				int n = sh.GetPropertyCount();
				for (int k = 0; k < n; k++)
				{
					string prop = sh.GetPropertyName(k);
					ShaderPropertyType ty = sh.GetPropertyType(k);
					log.Append(prop).Append(':').Append(ty).Append(' ');
					if (ty == ShaderPropertyType.Texture && (prop.StartsWith("unity_") || prop.IndexOf("Spray", StringComparison.OrdinalIgnoreCase) >= 0))
					{
						// lightmaps belong to Unity, and the spray-paint layer is drawn on
						// top of the paint (our camo there made parts pale and see-through)
						if (prop.IndexOf("Spray", StringComparison.OrdinalIgnoreCase) >= 0) m.SetTexture(prop, Clear());
					}
					else if (ty == ShaderPropertyType.Texture)
					{
						Texture cur = m.GetTexture(prop);
						if (cur != null) log.Append("(was ").Append(cur.name).Append(") ");
						m.SetTexture(prop, paint);
						m.SetTextureScale(prop, Vector2.one);
						m.SetTextureOffset(prop, Vector2.zero);
					}
					else if (ty == ShaderPropertyType.Color)
					{
						log.Append("(was ").Append(m.GetColor(prop)).Append(") ");
						m.SetColor(prop, Color.white);
					}
				}
			}
			catch (Exception e)
			{
				log.Append("property scan failed: ").Append(e.Message);
				foreach (string prop in new[] { "_BaseColor", "_Color", "_MainColor", "_Tint" })
					if (m.HasProperty(prop)) m.SetColor(prop, Color.white);
				foreach (string prop in new[] { "_BaseMap", "_MainTex" })
					if (m.HasProperty(prop)) m.SetTexture(prop, paint);
			}
			try { m.mainTexture = paint; } catch { }
			// draw as a solid, depth-writing object so parts don't show through each other
			int q0 = m.renderQueue;
			try
			{
				if (m.HasProperty("_QueueOffset")) m.SetFloat("_QueueOffset", 0f);
				foreach (string kw in new[] { "_SURFACE_TYPE_TRANSPARENT", "_ALPHABLEND_ON", "_ALPHAPREMULTIPLY_ON", "_BLENDMODE_ALPHA" })
					m.DisableKeyword(kw);
				if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 0f);
				if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 1f);
				if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", 1f);
				if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", 0f);
				m.SetOverrideTag("RenderType", "Opaque");
				m.renderQueue = 2000;
			}
			catch (Exception e) { log.Append(" opaque failed: ").Append(e.Message); }
			if (!_propsLogged) log.Append(" queue ").Append(q0).Append(" -> ").Append(m.renderQueue).Append(", keywords ").Append(string.Join(",", m.shaderKeywords));
			if (!_propsLogged) { _propsLogged = true; TLog.Info("3D model shader '" + m.shader.name + "' properties: " + log); }
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
			// Tyres read while the car is still settling (just spawned, on a slope, mid-bump) give a
			// tilted frame, which leaves the tank leaning in the car picture for good. The car model
			// is upright in its own space, so snap up to the model's up and forward to the nearest
			// flat axis.
			{
				Vector3 rawUp = up, rawFwd = fwd;
				up = Vector3.up;
				Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
				if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
				fwd = Mathf.Abs(flat.x) > Mathf.Abs(flat.z) ? new Vector3(Mathf.Sign(flat.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(flat.z));
				if (Vector3.Angle(rawUp, up) > 3f || Vector3.Angle(rawFwd, fwd) > 3f)
					TLog.Info($"3D tank: tyre frame up {rawUp} fwd {rawFwd} was tilted, using up {up} fwd {fwd}");
			}
			if (TankPlugin.Model3DFlip.Value) fwd = -fwd;
			if (wheelbase < 0.1f) wheelbase = 3f;
			float carLen = wheelbase * 1.55f;
			float scale = carLen * TankPlugin.Model3DSize.Value / HullLength;
			int layer = 0;
			try { layer = v.vehicleMeshRenderer != null ? v.vehicleMeshRenderer.gameObject.layer : parent.gameObject.layer; } catch { }
			// The car camera only films its own layer (VehicleModel) into the car's picture. On any
			// other layer the main camera draws the tank straight onto the map, unsorted and see-through.
			int camLayer = CamLayer(s.Cam);
			if (camLayer >= 0) layer = camLayer;

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
		if (TankPlugin.DrawDiagnostics.Value && s.Diags < 4)
		{
			if (s.NextDiag < 0f) s.NextDiag = now + 2f;
			else if (now >= s.NextDiag) { s.NextDiag = now + 15f; s.Diags++; Diag(v, s, s.Diags); }
		}
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
		Picture(v, s, k);
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


	private static float _grassSortAt = -1f;
	private static int _grassLayer, _grassOrder;
	private static bool _grassFound, _grassLogged;
	private static int _grassQueue = -1;

	// Tall grass in front of the player is a separate "front" grass mesh drawn above people.
	// Put the tank above that mesh too (layer and order), checked every few seconds.
	private static void GrassSort(ref int layer, ref int ord)
	{
		float now = Time.realtimeSinceStartup;
		if (now >= _grassSortAt)
		{
			_grassSortAt = now + 3f;
			_grassFound = false;
			try
			{
				TerrainGrassController g = TerrainGrassController.instance;
				var sb = _grassLogged ? null : new System.Text.StringBuilder("Grass draw order:");
				var rs = new System.Collections.Generic.List<Renderer>();
				var cells = g?.frontActiveCells;
				if (cells != null)
					for (int i = 0; i < cells.Count && i < 4; i++)
					{ var cell = cells[i]; if (cell == null) continue; rs.Add(cell.frontRenderer); rs.Add(cell.backRenderer); }
				_grassQueue = -1;
				foreach (Renderer r in rs)
				{
					if (r == null) continue;
					int q = r.sharedMaterial != null ? r.sharedMaterial.renderQueue : -1;
					if (q > _grassQueue) _grassQueue = q;
					sb?.Append($" {r.name} layer '{r.sortingLayerName}' ({SortingLayer.GetLayerValueFromID(r.sortingLayerID)}) order {r.sortingOrder} queue {q} unityLayer {r.gameObject.layer};");
					int lv = SortingLayer.GetLayerValueFromID(r.sortingLayerID);
					if (!_grassFound || lv > SortingLayer.GetLayerValueFromID(_grassLayer) || (r.sortingLayerID == _grassLayer && r.sortingOrder > _grassOrder))
					{ _grassLayer = r.sortingLayerID; _grassOrder = r.sortingOrder; _grassFound = true; }
				}
				if (sb != null && rs.Count == 0) sb.Append(" no grass cells yet;");
				if (sb != null && (rs.Count > 0 || now > 120f))
				{
					sb.Append($" player layer '{SortingLayer.IDToName(Tank.SortLayer)}' ({SortingLayer.GetLayerValueFromID(Tank.SortLayer)})");
					TLog.Info(sb.ToString());
					_grassLogged = true;
				}
			}
			catch (Exception e) { if (!_grassLogged) { _grassLogged = true; TLog.Warn("grass draw order: " + e.Message); } }
		}
		if (!_grassFound) return;
		int gl = SortingLayer.GetLayerValueFromID(_grassLayer), pl = SortingLayer.GetLayerValueFromID(layer);
		if (gl > pl) { layer = _grassLayer; ord = Mathf.Max(_grassOrder + 1, ord); }
		else if (gl == pl) ord = Mathf.Max(ord, _grassOrder + 1);
	}

	// lowest layer the car camera films
	private static int CamLayer(Camera c)
	{
		if (c == null) return -1;
		int m = c.cullingMask;
		if (m == 0 || m == -1) return -1;
		for (int i = 0; i < 32; i++) if (((m >> i) & 1) == 1) return i;
		return -1;
	}

	private static void SetLayer(GameObject go, int layer)
	{
		if (go == null || go.layer == layer) return;
		go.layer = layer;
	}

	/// <summary>Keeps the tank in the car camera's picture: on the camera's layer, inside its clip
	/// range (an orthographic camera may clip from behind itself), and at a picture size that
	/// keeps the zoomed-out tank sharp.</summary>
	private static void Picture(BasicVehicle v, State s, float k)
	{
		Camera c = s.Cam;
		if (c == null) return;
		try
		{
			int layer = CamLayer(c);
			if (layer >= 0)
			{
				SetLayer(s.Pivot, layer);
				SetLayer(s.Hull, layer);
				SetLayer(s.Turret, layer);
			}
			// the turret and gun swing past the camera's near plane at 0.3
			float near = -TankPlugin.Model3DNearClip.Value;
			if (c.orthographic && c.nearClipPlane > near) c.nearClipPlane = near;

			// Like the classic tank's art: draw the car picture on the player's sprite layer at a
			// high order, so tall grass and bushes don't cover it. The game sorts car pictures in
			// with the grass, which buried this big tank.
			if (TankPlugin.Model3DOnTop.Value && Tank.SortKnown)
			{
				Renderer qr = v.vehicleQuadRenderer;
				if (qr != null)
				{
					int sl = Tank.SortLayer, ord = TankPlugin.SortingOrder.Value;
					GrassSort(ref sl, ref ord);
					if (qr.sortingLayerID != sl) qr.sortingLayerID = sl;
					if (qr.sortingOrder != ord) qr.sortingOrder = ord;
					// The render queue wins over sorting layers: if grass draws in a later queue,
					// draw the tank picture in a queue after it.
					Material qm = qr.sharedMaterial;
					if (qm != null && _grassQueue >= 0)
					{
						int wq = Mathf.Max(_grassQueue + 1, 3000);
						if (qm.renderQueue < wq)
						{
							TLog.Info($"Tank picture queue {qm.renderQueue} -> {wq} (grass queue {_grassQueue})");
							qm.renderQueue = wq;
						}
					}
				}
			}
			int want = TankPlugin.Model3DPictureSize.Value;
			RenderTexture cur = c.targetTexture;
			if (want <= 0 || cur == null) return;
			if (s.Pic != null && cur.Pointer == s.Pic.Pointer) return;
			if (cur.width >= want && s.Pic == null) return;
			if (s.Pic == null)
			{
				s.Pic = new RenderTexture(want, want, cur.depth, cur.format);
				s.Pic.name = "zt_tank_picture";
				s.Pic.filterMode = cur.filterMode;
				s.Pic.antiAliasing = 1;
				s.Pic.Create();
				Object.DontDestroyOnLoad(s.Pic);
				TLog.Info($"3D tank picture: {cur.width}x{cur.height} {cur.format} -> {want}x{want} (camera zoom x{k:0.##})");
			}
			c.targetTexture = s.Pic;
			try { v.vehicleRenderTexture = s.Pic; } catch { }
			Renderer q = v.vehicleQuadRenderer;
			if (q != null)
				foreach (Material m in q.sharedMaterials)
					if (m != null && m.HasProperty("_MainTex")) m.SetTexture("_MainTex", s.Pic);
		}
		catch (Exception e) { TLog.Warn("3D tank picture: " + e.Message); }
	}

	// ------------------------------------------------------------------ draw diagnostics

	private static string Mat(Material m)
	{
		if (m == null) return "null";
		var b = new System.Text.StringBuilder();
		b.Append('\'').Append(m.name).Append("' shader '").Append(m.shader != null ? m.shader.name : "?").Append("' queue ").Append(m.renderQueue);
		try { b.Append(" keywords [").Append(string.Join(",", m.shaderKeywords)).Append(']'); } catch { }
		try { b.Append(" RenderType=").Append(m.GetTag("RenderType", false, "-")).Append(" Queue=").Append(m.GetTag("Queue", false, "-")); } catch { }
		try
		{
			Shader sh = m.shader;
			int n = sh.GetPropertyCount();
			for (int k = 0; k < n; k++)
			{
				string prop = sh.GetPropertyName(k);
				if (prop.StartsWith("unity_")) continue;
				ShaderPropertyType ty = sh.GetPropertyType(k);
				b.Append("\n      ").Append(prop).Append(" = ");
				switch (ty)
				{
					case ShaderPropertyType.Texture:
						Texture t = m.GetTexture(prop);
						b.Append(t == null ? "null" : $"'{t.name}' {t.width}x{t.height} {t.GetType().Name}");
						var rt = t != null ? t.TryCast<RenderTexture>() : null;
						if (rt != null) b.Append($" fmt {rt.format} depth {rt.depth} aa {rt.antiAliasing}");
						break;
					case ShaderPropertyType.Color: b.Append(m.GetColor(prop)); break;
					case ShaderPropertyType.Vector: b.Append(m.GetVector(prop)); break;
					default: b.Append(m.GetFloat(prop).ToString("0.###", CultureInfo.InvariantCulture)); break;
				}
			}
		}
		catch (Exception e) { b.Append(" (props: ").Append(e.Message).Append(')'); }
		return b.ToString();
	}

	private static string Rend(Renderer r)
	{
		if (r == null) return "null";
		var b = new System.Text.StringBuilder();
		b.Append($"'{Path(r.transform)}' {r.GetType().Name} enabled {r.enabled} active {r.gameObject.activeInHierarchy} layer {r.gameObject.layer} ({LayerMask.LayerToName(r.gameObject.layer)})");
		try { b.Append($" sorting '{r.sortingLayerName}'/{r.sortingOrder}"); } catch { }
		try { b.Append($" bounds c {r.bounds.center} size {r.bounds.size}"); } catch { }
		return b.ToString();
	}

	/// <summary>Logs how the car is drawn: its camera, the picture it renders into, the quad that
	/// shows that picture on the map, and every renderer on the car. For see-through problems.</summary>
	private static void Diag(BasicVehicle v, State s, int n)
	{
		var b = new System.Text.StringBuilder();
		b.Append($"Draw check {n}:");
		try
		{
			Camera c = s.Cam;
			if (c == null) b.Append("\n  camera: none");
			else
			{
				b.Append($"\n  camera '{Path(c.transform)}' enabled {c.enabled} clear {c.clearFlags} background {c.backgroundColor} mask {c.cullingMask} ({Layers(c.cullingMask)})");
				b.Append($"\n    ortho {c.orthographic} size {c.orthographicSize:0.###} near {c.nearClipPlane:0.###} far {c.farClipPlane:0.###} depth {c.depth} hdr {c.allowHDR} msaa {c.allowMSAA}");
				Vector3 lp = s.Pivot != null ? c.transform.InverseTransformPoint(s.Pivot.transform.position) : Vector3.zero;
				b.Append($"\n    tank pivot in camera space {lp} (z must be between near and far)");
				RenderTexture rt = c.targetTexture;
				if (rt == null) b.Append("\n    target: screen");
				else b.Append($"\n    target '{rt.name}' {rt.width}x{rt.height} fmt {rt.format} depth {rt.depth} aa {rt.antiAliasing} sRGB {rt.sRGB}");
				foreach (Renderer r in new[] { s.Hull != null ? s.Hull.GetComponent<Renderer>() : null, s.Turret != null ? s.Turret.GetComponent<Renderer>() : null })
				{
					if (r == null) continue;
					Bounds bb = r.bounds;
					Vector3 a = c.transform.InverseTransformPoint(bb.min), z = c.transform.InverseTransformPoint(bb.max);
					b.Append($"\n    {r.name} camera-space z {Mathf.Min(a.z, z.z):0.##}..{Mathf.Max(a.z, z.z):0.##}, in camera mask {((c.cullingMask >> r.gameObject.layer) & 1) == 1}");
				}
			}
		}
		catch (Exception e) { b.Append("\n  camera error: ").Append(e.Message); }
		try
		{
			Renderer q = v.vehicleQuadRenderer;
			b.Append("\n  quad ").Append(Rend(q));
			if (q != null) foreach (Material m in q.sharedMaterials) b.Append("\n    material ").Append(Mat(m));
		}
		catch (Exception e) { b.Append("\n  quad error: ").Append(e.Message); }
		try
		{
			Transform root = v.transform;
			b.Append($"\n  renderers under '{Path(root)}':");
			foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
			{
				bool ours = s.Ours.Contains(r.Pointer);
				b.Append("\n   ").Append(ours ? "[tank] " : "").Append(Rend(r));
				if (ours && n == 1) foreach (Material m in r.sharedMaterials) { b.Append("\n    material ").Append(Mat(m)); break; }
				else if (!ours && r.enabled) { Material m = r.sharedMaterial; if (m != null) b.Append($" material '{m.name}' ({(m.shader != null ? m.shader.name : "?")}) queue {m.renderQueue}"); }
			}
		}
		catch (Exception e) { b.Append("\n  renderer list error: ").Append(e.Message); }
		try
		{
			b.Append("\n  cameras:");
			foreach (Camera c in Camera.allCameras)
				b.Append($"\n    '{Path(c.transform)}' depth {c.depth} mask {Layers(c.cullingMask)} target {(c.targetTexture != null ? c.targetTexture.name : "screen")}");
		}
		catch (Exception e) { b.Append("\n  camera list error: ").Append(e.Message); }
		TLog.Info(b.ToString());
	}

	private static string Layers(int mask)
	{
		if (mask == -1) return "everything";
		var l = new List<string>();
		for (int i = 0; i < 32; i++) if (((mask >> i) & 1) == 1) { string nm = LayerMask.LayerToName(i); l.Add(i + (string.IsNullOrEmpty(nm) ? "" : ":" + nm)); }
		return string.Join(",", l);
	}
}
