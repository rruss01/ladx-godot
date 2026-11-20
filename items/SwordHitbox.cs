using Godot;

// The original game's sword hit detection, taken from LADX-Disassembly:
//   * tile check:    bank0.asm  CheckStaticSwordCollision (SwordCollisionMapX / SwordCollisionMapY)
//   * enemy hitbox:  bank2.asm  UpdateLinkAnimation (the LinkDirectionTo_* tables)
//
// Every offset here is in pixels from the TOP-LEFT corner of Link's 16x16 sprite (in the
// original, Link's position is that corner). Callers pass that corner in world space.
public static class SwordHitbox
{
	// Sword animation states, as the original numbers them.
	public const int StateNone = 0;
	public const int StateDraw = 1;         // first 3 frames of a swing
	public const int StateSwingStart = 2;   // next 3 frames
	public const int StateSwingMiddle = 3;  // last 8 frames
	public const int StateHolding = 5;      // sword held out (charging)

	// 0 = right, 1 = left, 2 = up, 3 = down: the original's order.
	public static int DirectionIndex(Vector2I facing) =>
		facing.X > 0 ? 0 : facing.X < 0 ? 1 : facing.Y < 0 ? 2 : 3;

	// ---- Tile check (bushes, grass, ...) ----
	// The game checks ONE point, 6 px beyond the middle of Link's edge in the facing direction.
	// It does this on every tick of a swing (draw, swing start, swing middle), but not while
	// the sword is just being held out.
	private static readonly Vector2[] TileProbe =
	{
		new Vector2(22, 8),  // right
		new Vector2(-6, 8),  // left
		new Vector2(8, -5),  // up
		new Vector2(8, 22),  // down
	};

	public static Vector2 TileProbePoint(Vector2I facing, Vector2 spriteTopLeft) =>
		spriteTopLeft + TileProbe[DirectionIndex(facing)];

	// ---- Enemy hitbox ----
	// A box given as its center and half-size, relative to Link's top-left corner.
	public readonly record struct Box(int CenterX, int CenterY, int HalfWidth, int HalfHeight);

	// [direction][state]; null = no hitbox in that state. The box exists only during
	// swing start, swing middle and holding.
	private static readonly Box?[][] Boxes =
	{
		// right
		new Box?[] { null, null, new Box(21, -5, 5, 5), new Box(25, 8, 10, 8), null, new Box(17, 8, 10, 8) },
		// left
		new Box?[] { null, null, new Box(-5, -5, 5, 5), new Box(-9, 8, 10, 8), null, new Box(-1, 8, 10, 8) },
		// up
		new Box?[] { null, null, new Box(21, -5, 5, 5), new Box(8, -8, 8, 8), null, new Box(8, -3, 8, 8) },
		// down
		new Box?[] { null, null, new Box(-5, 21, 5, 5), new Box(8, 24, 8, 8), null, new Box(8, 21, 8, 8) },
	};

	public static bool TryGetBox(Vector2I facing, int state, Vector2 spriteTopLeft, out Rect2 rect)
	{
		rect = default;
		if (state < 0 || state > 5)
			return false;

		Box? box = Boxes[DirectionIndex(facing)][state];
		if (box == null)
			return false;

		Box b = box.Value;
		rect = new Rect2(
			spriteTopLeft.X + b.CenterX - b.HalfWidth,
			spriteTopLeft.Y + b.CenterY - b.HalfHeight,
			b.HalfWidth * 2,
			b.HalfHeight * 2);
		return true;
	}

	// The spin attack uses a single 48x48 box centered on Link.
	public static Rect2 SpinBox(Vector2 spriteTopLeft) =>
		new Rect2(spriteTopLeft.X + 8 - 24, spriteTopLeft.Y + 8 - 24, 48, 48);
}
