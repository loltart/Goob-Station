using System.Linq;
using System.Numerics;
using Content.Goobstation.Shared.Gangwars.Components;
using Content.Shared.Inventory;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Circle = Robust.Shared.Maths.Circle;

namespace Content.Goobstation.Shared.Gangwars.Systems;

public sealed class GangwarRuleSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    public const int PointsPerTile = 10;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = AllEntityQuery<GangwarRuleComponent>();
        while (query.MoveNext(out var uid, out var rule))
        {
            if (_timing.CurTime < rule.NextScoreUpdate)
                continue;

            rule.NextScoreUpdate = _timing.CurTime + rule.ScoreUpdateInterval;
            rule.GangScores = CalculateScores();
            Dirty(uid, rule);
        }
    }

    /// <summary>
    /// Returns true if placing a territory would overlap an existing GangTerritoryComponent.
    /// Purposely gives more lee-way to each gangs own territory.
    /// </summary>
    public bool IsTerritoryTooClose(MapCoordinates location, float newRadius, Color? ownGangColor = null)
    {
        var worldPosition = location.Position;
        var query = AllEntityQuery<GangTerritoryComponent, TransformComponent>();
        while (query.MoveNext(out _, out var territory, out var xform))
        {
            var threshold = territory.TerritoryRadius + newRadius - (ownGangColor.HasValue && territory.GangColor == ownGangColor.Value ? 3f : 0f);
            if ((_transform.GetWorldPosition(xform) - worldPosition).Length() < threshold)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the number of gang clothes they are currently wearing.
    /// </summary>
    public int CountGangClothingSlots(EntityUid entity, Color? gangColor = null)
    {
        var slots = new[] { "shoes", "outerClothing", "jumpsuit", "head" };
        var count = 0;
        foreach (var slot in slots)
        {
            if (!_inventory.TryGetSlotEntity(entity, slot, out var item)
                || !TryComp<GangClothingComponent>(item, out var clothing)
                || gangColor.HasValue && clothing.Gang != gangColor.Value)
                continue;

            count++;
        }
        return count;
    }

    /// <summary>
    /// Returns true if the entity has at least 3 gang clothing pieces equipped,
    /// Purposely only requires 3 pieces due to oni's not wearing shoes.
    /// </summary>
    public bool IsWearingGangOutfit(EntityUid entity, Color? gangColor = null) =>
        CountGangClothingSlots(entity, gangColor) >= 3;

    /// <summary>
    /// Calculates total scores per gang: tiles x PointsPerTile + sum of each members GangPoints.
    /// </summary>
    public Dictionary<Color, int> CalculateScores()
    {
        var scores = new Dictionary<Color, int>();

        // Tile points
        foreach (var (color, tileCount) in GetTileCounts())
            scores[color] = tileCount * PointsPerTile;

        // Member gang points
        var memberQuery = AllEntityQuery<GangMemberComponent>();
        while (memberQuery.MoveNext(out _, out var member))
        {
            if (member.Gang is not { } gang)
                continue;

            scores[gang] = scores.GetValueOrDefault(gang) + member.GangPoints;
        }

        return scores;
    }

    /// <summary>
    /// Returns the number of unique tiles claimed by each gang color.
    /// Tiles shared by overlapping same-color territories are only counted once.
    /// </summary>
    public Dictionary<Color, int> GetTileCounts()
    {
        var seenTiles = new Dictionary<Color, HashSet<(EntityUid Grid, Vector2i Tile)>>();
        var territoryQuery = AllEntityQuery<GangTerritoryComponent, TransformComponent>();
        while (territoryQuery.MoveNext(out _, out var territory, out var xform))
        {
            if (territory.GangColor == Color.Transparent || xform.GridUid is not { } gridUid
                || !TryComp<MapGridComponent>(gridUid, out var grid) || !TryComp(gridUid, out TransformComponent? gridXform))
                continue;

            var (_, _, _, invWorldMatrix) = _transform.GetWorldPositionRotationMatrixWithInv(gridXform);
            var localPos = Vector2.Transform(_transform.GetWorldPosition(xform), invWorldMatrix);

            if (!seenTiles.TryGetValue(territory.GangColor, out var seen))
            {
                seen = new HashSet<(EntityUid, Vector2i)>();
                seenTiles[territory.GangColor] = seen;
            }

            foreach (var tile in _mapSystem.GetLocalTilesIntersecting(gridUid, grid, new Circle(localPos, territory.TerritoryRadius)))
                seen.Add((gridUid, tile.GridIndices));
        }

        return seenTiles.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);
    }
}
