namespace JordanMod;

class Debug
{

	public static void Log(string message)
	{
		Plugin.Log.LogInfo(message);
	}

	public static void LogWarning(string message)
	{
		Plugin.Log.LogWarning(message);
	}

	public static void LogError(string message)
	{
		Plugin.Log.LogError(message);
	}

}