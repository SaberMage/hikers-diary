using HarmonyLib;
using HikersDiary.Core;

namespace HikersDiary;

/// <summary>
/// The player colors for all saves (#6). The game keeps the colors in each save slot
/// (<c>PlayerLooks.LoadOne</c> and <c>ServerSaveLook</c>. These methods run only on the host.) The color book is one file for all
/// slots. When the game loads a color, the host gives the color from the book. When the game makes or
/// changes a color, the host writes it to the book.
/// </summary>
[HarmonyPatch(typeof(PlayerLooks))]
internal static class ColorPatches
{
    private static string _path;
    // The value is null if the host cannot read the book file. Then the game colors stay, and the host never writes over the file.
    internal static ColorBook Book;

    internal static void Open(string storeDir)
    {
        _path = System.IO.Path.Combine(storeDir, "colors.json");
        try
        {
            Book = ColorBook.Read(_path);
            Plugin.Logger.LogInfo($"Diary: color book has {Book.Players.Count} players.");
        }
        catch (Exception e)
        {
            Book = null;
            Plugin.Logger.LogError($"Diary: color book off. {e.Message}");
        }
    }

    // [impl->REQ-PLAYER-COLORS]
    [HarmonyPrefix, HarmonyPatch("LoadOne")]
    private static bool BeforeLoadOne(PlayerLooks __instance, PlayerLooks.LookPart lookPart)
    {
        PlayerNetworking net = __instance.playerCharacter?.playerNetworking;
        if (Book == null || net == null || string.IsNullOrEmpty(net.identifier)) return true;
        int color = Book.Get(net.identifier, (int)lookPart);
        if (color < 0) return true;
        // saveChange = true: the save slot also receives the color.
        net.ServerSetLook(color, lookPart, true);
        Plugin.Logger.LogInfo($"Diary: color book gave {net.identifier} {lookPart} = {color}.");
        return false;
    }

    // [impl->REQ-PLAYER-COLORS] The first color that the game gives a player goes to the book.
    [HarmonyPostfix, HarmonyPatch("LoadOne")]
    private static void AfterLoadOne(PlayerLooks __instance, PlayerLooks.LookPart lookPart) =>
        Record(__instance, lookPart, __instance.GetLookId(lookPart));

    // [impl->REQ-PLAYER-COLORS]
    [HarmonyPostfix, HarmonyPatch(nameof(PlayerLooks.ServerSaveLook))]
    private static void AfterSaveLook(PlayerLooks __instance, PlayerLooks.LookPart lookPart, int id) =>
        Record(__instance, lookPart, id);

    private static void Record(PlayerLooks looks, PlayerLooks.LookPart part, int color)
    {
        PlayerNetworking net = looks.playerCharacter?.playerNetworking;
        if (Book == null || net == null || string.IsNullOrEmpty(net.identifier)) return;
        if (!Book.Set(net.identifier, net.userPlatformIdString, (int)part, color)) return;
        try
        {
            Book.Write(_path);
            Plugin.Logger.LogInfo($"Diary: color book: {net.identifier} {part} = {color}.");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"Diary: color book write failed. {e.Message}");
        }
    }
}
