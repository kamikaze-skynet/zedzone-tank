using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZedTank;

internal static class TankFx
{
	private enum Kind
	{
		Tracer,
		Flash,
		Puff,
		Casing
	}

	private class Fx1
	{
		public GameObject Go;

		public SpriteRenderer Sr;

		public Kind K;

		public float T0;

		public float Dur;

		public Vector2 A;

		public Vector2 B;

		public Vector2 Vel;

		public float Rot;

		public float Spin;

		public float S0;

		public float S1;

		public float Fall;

		public Color C;
	}

	private static readonly List<Fx1> _fx = new List<Fx1>();

	private static Sprite _streak;

	private static Sprite _flash;

	private static Sprite _puff;

	private static Sprite _casing;

	private static bool _made;

	private static Sprite Make(int w, int h, Func<int, int, Color> px, Vector2 pivot)
	{
		Texture2D val = new Texture2D(w, h, (TextureFormat)4, false)
		{
			filterMode = (FilterMode)0,
			wrapMode = (TextureWrapMode)1
		};
		((Object)val).hideFlags = (HideFlags)61;
		for (int i = 0; i < h; i++)
		{
			for (int j = 0; j < w; j++)
			{
				val.SetPixel(j, i, px(j, i));
			}
		}
		val.Apply();
		Sprite obj = Sprite.Create(val, new Rect(0f, 0f, (float)w, (float)h), pivot, 32f);
		((Object)obj).hideFlags = (HideFlags)61;
		return obj;
	}

	private static void Ensure()
	{
		if (_made)
		{
			return;
		}
		_made = true;
		_streak = Make(16, 3, delegate(int x, int y)
		{
			float num7 = (float)x / 15f;
			float num8 = num7 * num7;
			if (y != 1)
			{
				num8 *= 0.4f;
			}
			Color result2 = Color.Lerp(new Color(1f, 0.45f, 0.1f), new Color(1f, 0.97f, 0.75f), num7);
			result2.a = num8;
			return result2;
		}, new Vector2(1f, 0.5f));
		_flash = Make(15, 15, delegate(int x, int y)
		{
			int num3 = x - 4;
			int num4 = y - 7;
			float num5 = Mathf.Sqrt((float)(num3 * num3 + num4 * num4));
			float num6 = 0f;
			Color result = default(Color);
			result = new Color(1f, 0.85f, 0.35f);
			if (num5 <= 1.6f)
			{
				num6 = 1f;
				result = new Color(1f, 1f, 0.9f);
			}
			else if (num4 == 0 && num3 > 0 && num3 <= 10)
			{
				num6 = 1f - (float)num3 / 11f;
			}
			else if (num4 == 0 && num3 < 0 && num3 >= -3)
			{
				num6 = 0.7f - (float)(-num3) / 5f;
			}
			else if (num3 == 0 && Math.Abs(num4) <= 4)
			{
				num6 = 0.9f - (float)Math.Abs(num4) / 5f;
			}
			else if (Math.Abs(num3) == Math.Abs(num4) && num3 > 0 && num3 <= 4)
			{
				num6 = 0.75f - (float)num3 / 5f;
			}
			else if (num5 <= 2.6f)
			{
				num6 = 0.55f;
				result = new Color(1f, 0.6f, 0.2f);
			}
			result.a = Mathf.Clamp01(num6);
			return result;
		}, new Vector2(0.3f, 0.5f));
		_puff = Make(7, 7, delegate(int x, int y)
		{
			float num = Mathf.Sqrt((float)((x - 3) * (x - 3) + (y - 3) * (y - 3)));
			float num2 = ((num <= 2f) ? 1f : ((num <= 3.2f) ? 0.45f : 0f));
			return new Color(1f, 1f, 1f, num2);
		}, new Vector2(0.5f, 0.5f));
		_casing = Make(3, 2, (int x, int y) => (x != 0) ? ((y != 1) ? new Color(0.85f, 0.66f, 0.26f) : new Color(0.98f, 0.84f, 0.45f)) : new Color(0.62f, 0.45f, 0.16f), new Vector2(0.5f, 0.5f));
	}

	private static Fx1 Add(Kind k, Sprite sp, int layer, int order, float t0, float dur)
	{
		GameObject val = new GameObject("zpi_fx");
		SpriteRenderer val2 = val.AddComponent<SpriteRenderer>();
		val2.sprite = sp;
		((Renderer)val2).sortingLayerID = layer;
		((Renderer)val2).sortingOrder = order;
		if (t0 > Time.realtimeSinceStartup)
		{
			((Renderer)val2).enabled = false;
		}
		Fx1 fx = new Fx1
		{
			Go = val,
			Sr = val2,
			K = k,
			T0 = t0,
			Dur = dur
		};
		_fx.Add(fx);
		return fx;
	}

	public static void Shot(Vector2 muzzle, Vector2 target, bool hit, float heading, float altOffset, int layer, Color? tint = null, bool casing = true)
	{
		Ensure();
		float realtimeSinceStartup = Time.realtimeSinceStartup;
		float num = Mathf.Clamp(TankPlugin.FxScale.Value, 0.3f, 5f);
		Vector2 val = target - muzzle;
		float magnitude = val.magnitude;
		float num2 = Mathf.Atan2(val.y, val.x) * 57.29578f;
		float num3 = Mathf.Clamp(magnitude / 220f, 0.035f, 0.09f);
		Fx1 fx = Add(Kind.Tracer, _streak, layer, 32016, realtimeSinceStartup, num3);
		fx.A = muzzle;
		fx.B = target;
		fx.Rot = num2;
		fx.S0 = 2.2f * num;
		fx.S1 = num;
		fx.C = (tint ?? Color.white);
		Fx1 fx2 = Add(Kind.Flash, _flash, layer, 32020, realtimeSinceStartup, 0.05f);
		fx2.A = muzzle;
		fx2.Rot = num2 + Random.Range(-8f, 8f);
		fx2.S0 = Random.Range(0.85f, 1.25f) * num;
		fx2.C = (tint ?? Color.white);
		int num4 = (hit ? 4 : 3);
		for (int i = 0; i < num4; i++)
		{
			Fx1 fx3 = Add(Kind.Puff, _puff, layer, 32005, realtimeSinceStartup + num3, Random.Range(0.25f, 0.42f));
			fx3.A = target + Random.insideUnitCircle * 0.12f * num;
			fx3.Vel = (-val.normalized * 0.6f + Random.insideUnitCircle) * (hit ? 1.6f : 1.1f) * Mathf.Sqrt(num);
			fx3.S0 = (hit ? 0.45f : 0.55f) * num;
			fx3.S1 = (hit ? 0.75f : 1.35f) * num;
			fx3.C = (hit ? new Color(0.62f, 0.06f, 0.05f, 0.95f) : new Color(0.66f, 0.6f, 0.5f, 0.8f));
		}
		if (casing && Random.value < 0.7f)
		{
			float num5 = heading * ((float)Math.PI / 180f);
			Vector2 val2 = default(Vector2);
			val2 = new Vector2(Mathf.Sin(num5), 0f - Mathf.Cos(num5));
			Vector2 val3 = default(Vector2);
			val3 = new Vector2(Mathf.Cos(num5), Mathf.Sin(num5));
			Fx1 fx4 = Add(Kind.Casing, _casing, layer, 32014, realtimeSinceStartup, Random.Range(0.45f, 0.6f));
			fx4.A = muzzle - val3 * 0.6f;
			fx4.Vel = val2 * Random.Range(1.6f, 2.6f) + val3 * Random.Range(-0.4f, 0.2f);
			fx4.Rot = Random.Range(0f, 360f);
			fx4.Spin = Random.Range(600f, 1100f) * ((Random.value < 0.5f) ? (-1f) : 1f);
			fx4.S0 = 1.2f * num;
			fx4.Fall = altOffset;
			fx4.C = Color.white;
		}
	}

	// Main gun blast: a big muzzle flash and a ring of smoke that drifts out.
	public static void Blast(Vector2 muzzle, float angle, int layer)
	{
		Ensure();
		float now = Time.realtimeSinceStartup;
		float k = Mathf.Clamp(TankPlugin.FxScale.Value, 0.3f, 5f);
		Fx1 f = Add(Kind.Flash, _flash, layer, 32020, now, 0.09f);
		f.A = muzzle;
		f.Rot = angle;
		f.S0 = 2.6f * k;
		f.C = Color.white;
		Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
		for (int i = 0; i < 10; i++)
		{
			Fx1 p = Add(Kind.Puff, _puff, layer, 32005, now + Random.Range(0f, 0.05f), Random.Range(0.6f, 1.1f));
			p.A = muzzle + Random.insideUnitCircle * 0.2f * k;
			p.Vel = (dir * Random.Range(0.3f, 2.2f) + Random.insideUnitCircle * 1.4f) * Mathf.Sqrt(k);
			p.S0 = 0.8f * k;
			p.S1 = Random.Range(2.2f, 3.4f) * k;
			float g = Random.Range(0.55f, 0.75f);
			p.C = new Color(g, g * 0.97f, g * 0.9f, 0.85f);
		}
	}

	public static void Tick()
	{
		float realtimeSinceStartup = Time.realtimeSinceStartup;
		for (int num = _fx.Count - 1; num >= 0; num--)
		{
			Fx1 fx = _fx[num];
			float num2 = (realtimeSinceStartup - fx.T0) / Mathf.Max(0.001f, fx.Dur);
			if (num2 >= 1f || (Object)(object)fx.Go == (Object)null)
			{
				try
				{
					if ((Object)(object)fx.Go != (Object)null)
					{
						Object.Destroy((Object)(object)fx.Go);
					}
				}
				catch
				{
				}
				_fx.RemoveAt(num);
			}
			else if (!(num2 < 0f))
			{
				try
				{
					if (!((Renderer)fx.Sr).enabled)
					{
						((Renderer)fx.Sr).enabled = true;
					}
					Transform transform = fx.Go.transform;
					float num3 = realtimeSinceStartup - fx.T0;
					Color c = fx.C;
					switch (fx.K)
					{
					case Kind.Tracer:
						transform.position = ((Vector2)(Vector2.Lerp(fx.A, fx.B, num2)));
						transform.rotation = Quaternion.Euler(0f, 0f, fx.Rot);
						transform.localScale = new Vector3(fx.S0, fx.S1, 1f);
						break;
					case Kind.Flash:
					{
						transform.position = ((Vector2)(fx.A));
						transform.rotation = Quaternion.Euler(0f, 0f, fx.Rot);
						float num6 = fx.S0 * (1f - num2 * 0.3f);
						transform.localScale = new Vector3(num6, num6, 1f);
						c.a *= 1f - num2;
						break;
					}
					case Kind.Puff:
					{
						float num4 = 1f - num2;
						transform.position = ((Vector2)(fx.A + fx.Vel * num3 * (0.6f + 0.4f * num4)));
						float num5 = Mathf.Lerp(fx.S0, fx.S1, Mathf.Sqrt(num2));
						transform.localScale = new Vector3(num5, num5, 1f);
						c.a *= num4 * num4;
						break;
					}
					case Kind.Casing:
					{
						Vector2 val = fx.A + fx.Vel * num3 * (1f - num2 * 0.5f) - new Vector2(0f, fx.Fall * num2 * num2);
						transform.position = ((Vector2)(val));
						transform.rotation = Quaternion.Euler(0f, 0f, fx.Rot + fx.Spin * num3);
						transform.localScale = new Vector3(fx.S0, fx.S0, 1f);
						if (num2 > 0.7f)
						{
							c.a *= (1f - num2) / 0.3f;
						}
						break;
					}
					}
					fx.Sr.color = c;
				}
				catch
				{
				}
			}
		}
	}
}
