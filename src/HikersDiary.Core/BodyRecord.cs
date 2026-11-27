namespace HikersDiary.Core;

/// <summary>The saved state of one body (game class <c>Corpse</c>) of a player who left.</summary>
public sealed record BodyRecord(
    string Identifier,
    float[] Position,
    float[] Rotation,
    int HeadColor,
    int TorsoColor,
    int LegsColor);
