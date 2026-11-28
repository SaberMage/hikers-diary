namespace HikersDiary.Core;

/// <summary>
/// This record holds the saved state of one player (#5). <see cref="Head"/> is the game
/// <c>headState</c> (2 values). <see cref="PosePath"/> is the GameObject path of the game
/// <c>PlayerPose</c> (a seat), or empty. <see cref="Items"/> holds the held item and the items in
/// the pockets, by slot name (<see cref="Hand"/> or one of <see cref="Pockets"/>). Each item has
/// the keys of a <see cref="PropRecord"/>.
/// </summary>
public sealed record PlayerRecord(
    string Identifier,
    float[] Position,
    float[] Rotation,
    float[] Head,
    float Crouch,
    bool Sitting,
    string PosePath,
    Dictionary<string, PropRecord> Items)
{
    public const string Hand = "hand";
    public static readonly string[] Pockets = { "backpack", "holster", "blindfold" };
}
