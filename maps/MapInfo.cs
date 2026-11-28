using Godot;

// Put this script on the ROOT node of every map scene (overworld, dungeon floor, house...).
// It describes the map so the persistent systems (camera, collision, interactive tiles)
// can configure themselves when MapManager loads it.
//
// Tile-based maps (houses, shops): set Min/Max Room to (0, 0) and leave the image-based
// group empty. Put a physics layer on the map's own TileSet for walls.
public partial class MapInfo : Node2D
{
	// Unique name, e.g. "overworld" or "links_house". Permanent changes are saved per map id.
	[Export] public string MapId { get; set; } = "";
	[Export] public TileMapLayer Animated { get; set; }
	[Export(PropertyHint.File, "*.json")] public string AnimatedTilesTable { get; set; } = "";

	// The rooms this map spans, as grid coordinates. A house or shop is one room: (0,0) to (0,0).
	[Export] public Vector2I MinRoom { get; set; } = Vector2I.Zero;
	[Export] public Vector2I MaxRoom { get; set; } = Vector2I.Zero;
	[Export] public Vector2I RoomSize { get; set; } = new Vector2I(160, 128);

	[ExportGroup("Image-based maps (overworld, dungeons)")]
	[Export] public Texture2D CollisionMask { get; set; }  // white = solid, one block per room
	[Export] public Texture2D TileIds { get; set; }        // one pixel per 16x16 cell
	[Export(PropertyHint.File, "*.json")] public string TileLabels { get; set; } = "";
	[Export] public TileMapLayer Objects { get; set; }     // 16x16 layer inside this scene

	[ExportGroup("Music")]
	// On: the song depends on the room (OverworldMusic's grid). Off: 'Music' plays everywhere.
	[Export] public bool RegionMusic { get; set; } = false;
	[Export] public MusicTrack Music { get; set; } = MusicTrack.None;

	// Every doorway, staircase and cave mouth in this map. WarpManager spawns the ones
	// for the current room only.
	[ExportGroup("Warps")]
	[Export] public Godot.Collections.Array<WarpData> Warps { get; set; } = new();

	public override void _Ready()
	{
		if (MapId == "")
			GD.PushWarning($"MapInfo on '{Name}' has no Map Id; permanent changes won't be kept apart.");
	}
}
