using Godot;

// Builds wall collision for the CURRENT room from ONE big black-and-white mask
// image covering the whole overworld (white = solid, black = walkable).
//
// The mask is laid out exactly like the room grid: room (x, y) occupies the
// block at column x, row y. For a 16x16 world of 160x128 rooms that is
// 2560x2048 pixels. Only the current room's tiles exist at any time.
public partial class CollisionLoader : Node
{
	[Export] public ScreenScroller Scroller { get; set; }

	// A TileMapLayer at position (0, 0) whose TileSet has a physics layer
	// and one solid tile. Leave it empty; this script fills it.
	[Export] public TileMapLayer Collision { get; set; }

	// The whole-overworld mask (e.g. res://overworld_mask.png).
	[Export] public Texture2D MaskTexture { get; set; }

	// Must match ScreenScroller.RoomSize and your collision TileSet's tile size.
	[Export] public Vector2I RoomSize { get; set; } = new Vector2I(160, 128);
	[Export] public Vector2I TileSize { get; set; } = new Vector2I(8, 8);

	// A tile becomes solid if at least this fraction of its mask pixels are white.
	[Export(PropertyHint.Range, "0.05,1,0.05")] public float SolidThreshold { get; set; } = 0.5f;

	// Which tile in the collision TileSet is the solid one.
	[Export] public int SolidSourceId { get; set; } = 0;
	[Export] public Vector2I SolidAtlasCoords { get; set; } = Vector2I.Zero;

	private Image _mask;
	private Vector2I _roomsWide; // how many rooms the mask covers (columns, rows)
	private Vector2I _maskPerRoom; // mask pixels per room (width, height)

	public override void _Ready()
	{
		// Wait a moment so the camera has finished placing itself in its first room.
		CallDeferred(MethodName.Init);
	}

	private void Init()
	{
		_mask = MaskTexture.GetImage();
		if (_mask.IsCompressed())
			_mask.Decompress();

		// The mask covers the same range of rooms the camera is allowed to visit.
		_roomsWide = Scroller.MaxRoom - Scroller.MinRoom + Vector2I.One;

		if (_mask.GetWidth() % _roomsWide.X != 0 || _mask.GetHeight() % _roomsWide.Y != 0)
		{
			GD.PushWarning($"CollisionLoader: mask is {_mask.GetWidth()}x{_mask.GetHeight()}, " +
				$"which doesn't divide evenly into {_roomsWide.X}x{_roomsWide.Y} rooms.");
			return;
		}
		_maskPerRoom = new Vector2I(_mask.GetWidth() / _roomsWide.X, _mask.GetHeight() / _roomsWide.Y);

		Scroller.RoomChanged += LoadRoom;
		LoadRoom(Scroller.CurrentRoom);
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(Scroller))
			Scroller.RoomChanged -= LoadRoom;
	}

	private void LoadRoom(Vector2I room)
	{
		Collision.Clear();

		// Where this room's block starts inside the big mask.
		Vector2I rel = room - Scroller.MinRoom;
		Vector2I maskOrigin = new Vector2I(rel.X * _maskPerRoom.X, rel.Y * _maskPerRoom.Y);

		Vector2I tiles = new Vector2I(RoomSize.X / TileSize.X, RoomSize.Y / TileSize.Y); // 20 x 16

		// The mask can be any size that divides evenly into the tile grid:
		// 1 px per tile, 8 px per tile (full resolution), and so on.
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
