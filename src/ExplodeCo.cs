using System;
using System.Reflection;
using HarmonyLib;

namespace ZedTank;

// The game applies blast damage in BasicExplosion.ExplodeDamage, a coroutine whose compiler
// name ("<ExplodeDamage>d__69", "d__73", ...) changes whenever the game adds code to
// BasicExplosion. Game patch 1.0.22 renamed it, so it is found by name prefix and read by
// reflection instead of being compiled in.
internal static class ExplodeCo
{
	internal static Type Type;
	private static ConstructorInfo _ctor;
	private static PropertyInfo _this;

	internal static MethodInfo MoveNext()
	{
		try
		{
			foreach (Type t in typeof(BasicExplosion).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
				if (t.Name.StartsWith("_ExplodeDamage_d__", StringComparison.Ordinal)) { Type = t; break; }
			if (Type == null) return null;
			_ctor = Type.GetConstructor(new[] { typeof(IntPtr) });
			_this = Type.GetProperty("__4__this");
			return _ctor != null && _this != null ? AccessTools.Method(Type, "MoveNext") : null;
		}
		catch { return null; }
	}

	internal static BasicExplosion Owner(Il2CppSystem.Object co)
	{
		try
		{
			if (co == null || _ctor == null || _this == null || co.Pointer == IntPtr.Zero) return null;
			return _this.GetValue(_ctor.Invoke(new object[] { co.Pointer })) as BasicExplosion;
		}
		catch { return null; }
	}
}
