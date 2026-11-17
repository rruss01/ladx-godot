using Godot;

// Builds wall collision for the CURRENT room of the CURRENT map from a black-and-white
// mask image (white = solid, black = walkable), one block of the image per room.
//
// MapManager calls SetMap() whenever a map loads. A map without a mask (a tile-based
// house, say) gets no generated collision here: its own tiles provide it.
public partial class CollisionLoader : Node
{
	[Export] public ScreenScroller Scroller { get; set; }

	// A TileMapLayer at position (0, 0) whose TileSet has a physics layer
	// and one solid tile. Leave it empty; this script fills it.
	[Export] public TileMapLayer Collision { get; set; }

	// Must match your collision TileSet's tile size.
	[Export] public Vector2I TileSize { get; set; } = new Vector2I(8, 8);

	// A tile becomes solid if at least this fraction of its mask pixels are white.
	[Export(PropertyHint.Range, "0.05,1,0.05")] public float SolidThreshold { get; set; } = 0.5f;

	// Fires after a room's collision tiles are in place (even if the room has no walls).
	[Signal] public delegate void RoomLoadedEventHandler(Vector2I room);

	// Which tile in the collision TileSet is the solid one.
	[Export] public int SolidSourceId { get; set; } = 0;
	[Export] public Vector2I SolidAtlasCoords { get; set; } = Vector2I.Zero;

	// Taken from the current map by SetMap(); not set in the Inspector.
	public Vector2I RoomSize { get; private set; } = new Vector2I(160, 128);

	private Image _mask;
	private Vector2I _minRoom;
	private Vector2I _maskPerRoom;

	public override void _Ready()
	{
		Scroller.RoomChanged += LoadRoom;
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(Scroller))
			Scroller.RoomChanged -= LoadRoom;
	}

	public void SetMap(MapInfo map)
	{
		Collision.Clear();
		_mask = null;
		RoomSize = map.RoomSize;
		_minRoom = map.MinRoom;

		if (map.CollisionMask == null)
			return; // tile-based map: nothing to generate

		Image image = map.CollisionMask.GetImage();
		if (image.IsCompressed())
			image.Decompress();

		Vector2I rooms = map.MaxRoom - map.MinRoom + Vector2I.One;
		if (image.GetWidth() % rooms.X != 0 || image.GetHeight() % rooms.Y != 0)
		{
			GD.PushWarning($"CollisionLoader: mask is {image.GetWidth()}x{image.GetHeight()}, " +
				$"which doesn't divide evenly into {rooms.X}x{rooms.Y} rooms.");
			return;
		}

		_maskPerRoom = new Vector2I(image.GetWidth() / rooms.X, image.GetHeight() / rooms.Y);
		_mask = image;
	}

	private void LoadRoom(Vector2I room)
	{
		BuildRoom(room);
		EmitSignal(SignalName.RoomLoaded, room);
	}

	private void BuildRoom(Vector2I room)
	{
		Collision.Clear();
		if (_mask == null)
			return;

		// Where this room's block starts inside the big mask.
		Vector2I rel = room - _minRoom;
		Vector2I maskOrigin = new Vector2I(rel.X * _maskPerRoom.X, rel.Y * _maskPerRoom.Y);

		Vector2I tiles = new Vector2I(RoomSize.X / TileSize.X, RoomSize.Y / TileSize.Y); // 20 x 16

		// The mask can be any size that divides evenly into the tile grid.
		if (_maskPerRoom.X % tiles.X != 0 || _maskPerRoom.Y % tiles.Y != 0)
		{
			GD.PushWarning($"CollisionLoader: {_maskPerRoom.X}x{_maskPerRoom.Y} mask pixels per room " +
				$"doesn't divide evenly into {tiles.X}x{tiles.Y} tiles.");
			return;
		}

		int blockW = _maskPerRoom.X / tiles.X;
		int blockH = _maskPerRoom.Y / tiles.Y;
		int blockArea = blockW * blockH;
		Vector2I tileOrigin = new Vector2I(room.X * tiles.X, room.Y * tiles.Y);

		for (int ty = 0; ty < tiles.Y; ty++)
		{
			for (int tx = 0; tx < tiles.X; tx++)
			{
				int white = 0;
				for (int py = 0; py < blockH; py++)
				{
					for (int px = 0; px < blockW; px++)
					{
						Color c = _mask.GetPixel(
							maskOrigin.X + tx * blockW + px,
							maskOrigin.Y + ty * blockH + py);
						// Opaque and bright = solid.
						if (c.A > 0.5f && (c.R + c.G + c.B) / 3f > 0.5f)
							white++;
					}
				}

				if ((float)white / blockArea >= SolidThreshold)
					Collision.SetCell(tileOrigin + new Vector2I(tx, ty), SolidSourceId, SolidAtlasCoords);
			}
		}
	}
}
