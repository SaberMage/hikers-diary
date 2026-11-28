namespace HikersDiary.Core;

/// <summary>
/// The saved state of one body (game class <c>Corpse</c>) of a player who left. <see cref="Items"/>
/// holds the items in the homes of the body, by the slot names of <see cref="PlayerRecord"/> (#5).
/// It is null in a file of version 4 or older. <see cref="Platform"/> is the path of the moving platform
/// that the body is on, or null. If it is not null, the position and the rotation are relative to it.
/// </summary>
public sealed record BodyRecord(
    string Identifier,
    float[] Position,
    float[] Rotation,
    int HeadColor,
    int TorsoColor,
    int LegsColor,
    Dictionary<string, PropRecord>? Items = null,
    string? Platform = null);
