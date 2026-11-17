namespace ZedTank;

// Small public entry point for other mods (Spawn Menu's Tank kit calls it by reflection).
public static class TankApi
{
	/// <summary>Puts a tank in front of the local player. A result starting with '!' means it failed.</summary>
	public static string CallIn(bool ignoreLimit)
	{
		if (!TankRunner.Active) return "!Tank mod is off (disabled in its config)";
		GameController gc = GameController.instance;
		if (gc == null || gc.playerCharacter == null) return "!Load into a world first";
		return Tank.CallIn(gc.playerCharacter, ignoreLimit);
	}

	/// <summary>The item id of the main gun's rockets (the game's recoilless round), or -1.</summary>
	public static int RocketItemId()
	{
		return Rockets.Find() ? Rockets.AmmoId : -1;
	}

	/// <summary>Gives the local player full cans of gasoline (the tank's fuel, same as cars).</summary>
	public static string GiveFuelCans(int count)
	{
		return Gas.GiveCans(count);
	}
}
