using System.Numerics;
using Content.Goobstation.Shared.Gangwars.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Circle = Robust.Shared.Maths.Circle;

namespace Content.Goobstation.Client.Gangwars;

/// <summary>
/// Draws a filled + outlined territory around every GangTerritoryComponent entity.
/// Only renders when the local player has GangMemberComponent.
/// </summary>
public sealed class GangTerritoryOverlay(IEntityManager entManager) : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    private readonly IPlayerManager _playerManager = IoCManager.Resolve<IPlayerManager>();
    private readonly TransformSystem _transform = entManager.System<TransformSystem>();
    private readonly SharedMapSystem _mapSystem = entManager.System<SharedMapSystem>();

    // Groups tiles by (grid, color) so touching territories with the same color don't have an outline.
    private readonly Dictionary<(EntityUid, Color), (Matrix3x2 WorldMatrix, float TileSize, HashSet<Vector2i> Tiles)> _groups = new();

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _playerManager.LocalEntity is { } local
            && entManager.TryGetComponent<GangMemberComponent>(local, out var member)
            && member.OverlayVisible;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        _groups.Clear();
        CollectTiles(args.MapId);
        DrawGroups(args.WorldHandle);
    }

    private void CollectTiles(MapId mapId)
    {
        var query = entManager.EntityQueryEnumerator<GangTerritoryComponent, TransformComponent>();
        while (query.MoveNext(out _, out var territoryComp, out var xform))
        {
            if (xform.MapID != mapId
                || xform.GridUid is not { } gridUid
                || territoryComp.GangColor == Color.Transparent)
                continue;

            if (!entManager.TryGetComponent(gridUid, out MapGridComponent? grid)
                || !entManager.TryGetComponent(gridUid, out TransformComponent? gridXform))
                continue;

            var (_, _, worldMatrix, invWorldMatrix) = _transform.GetWorldPositionRotationMatrixWithInv(gridXform);
            var key = (gridUid, territoryComp.GangColor);

            if (!_groups.TryGetValue(key, out var group))
            {
                group = (worldMatrix, grid.TileSize, new HashSet<Vector2i>());
                _groups[key] = group;
            }

            var localPos = Vector2.Transform(_transform.GetWorldPosition(xform), invWorldMatrix);
            var circle = new Circle(localPos, territoryComp.TerritoryRadius);

            foreach (var tile in _mapSystem.GetLocalTilesIntersecting(gridUid, grid, circle))
                group.Tiles.Add(tile.GridIndices);
        }
    }

    private void DrawGroups(DrawingHandleWorld handle)
    {
        foreach (var ((_, color), (worldMatrix, tileSize, tiles)) in _groups)
        {
            handle.SetTransform(worldMatrix);

            foreach (var tileIndex in tiles)
                DrawTile(handle, tileIndex, tileSize, tiles, color);

            handle.SetTransform(Matrix3x2.Identity);
        }
    }

    private static void DrawTile(DrawingHandleWorld handle, Vector2i tileIndex, float tileSize, HashSet<Vector2i> tiles, Color color)
    {
        var left = tileIndex.X * tileSize;
        var bottom = tileIndex.Y * tileSize;
        var right = left + tileSize;
        var top = bottom + tileSize;

        handle.DrawRect(new Box2(left, bottom, right, top), color.WithAlpha(0.12f));

        var borderColor = color.WithAlpha(0.85f);

        if (!tiles.Contains(new Vector2i(tileIndex.X, tileIndex.Y - 1))) handle.DrawLine(new Vector2(left, bottom), new Vector2(right, bottom), borderColor); // bottom
        if (!tiles.Contains(new Vector2i(tileIndex.X, tileIndex.Y + 1))) handle.DrawLine(new Vector2(left, top),new Vector2(right, top), borderColor); // top
        if (!tiles.Contains(new Vector2i(tileIndex.X - 1, tileIndex.Y))) handle.DrawLine(new Vector2(left, bottom), new Vector2(left, top), borderColor); // left
        if (!tiles.Contains(new Vector2i(tileIndex.X + 1, tileIndex.Y))) handle.DrawLine(new Vector2(right, bottom), new Vector2(right, top), borderColor); // right
    }
}
