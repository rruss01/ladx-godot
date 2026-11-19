using Godot;

// One doorway, staircase or cave mouth, stored as plain data on a MapInfo.
// Nothing here is a node: WarpManager turns the warps of the CURRENT room into
// WarpTrigger nodes, and throws them away again when the screen changes.
[GlobalClass]
public partial class WarpData : Resource
{
	// Where the doorway is.
	[ExportGroup("Source")]
	[Export] public Vector2I Room { get; set; } = Vector2I.Zero;

	// The trigger's rectangle, in pixels from the top-left of THAT room (0,0 - 160,128).
	[Export] public Rect2I Area { get; set; } = new Rect2I(0, 0, 16, 16);

	// Where it leads. Same meaning as the fields on WarpTrigger.
	[ExportGroup("Destination")]
	[Export(PropertyHint.File, "*.tscn")] public string DestinationMap { get; set; } = "";
	[Export] public Vector2I DestinationRoom { get; set; } = Vector2I.Zero;
	[Export] public Vector2I DestinationScreenPosition { get; set; } = new Vector2I(80, 64);
}
