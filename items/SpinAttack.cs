using Godot;

// The spin attack's data, worked out from the original's tables in LADX-Disassembly
// (bank2.asm: LinkDirectionToAbsolute, LinkDirectionToSwordAnimationState, LinkDirectionToSwordDirection).
//
// The original does NOT rotate anything with maths. It has a lookup table with 8 entries for each
// way Link can face, and each entry says "turn Link's body to face THIS way and draw the sword
// THIS way". We resolved those tables once (with a script) so this file holds the final answer.
//
// Each row below is in TIME ORDER: entry 0 is the first thing you see after letting go of the button.
public static class SpinAttack
{
	// The original's sword directions, numbered clockwise starting from the right (SWORD_DIRECTION_*).
	public const int Right = 0;
	public const int RightBottom = 1;
	public const int Bottom = 2;
	public const int LeftBottom = 3;
	public const int Left = 4;
	public const int LeftTop = 5;
	public const int Top = 6;
	public const int RightTop = 7;

	// Where the sword points on each step. Row = which way Link was facing: right, left, up, down
	// (the same order as SwordHitbox.DirectionIndex).
	//
	// Look at the numbers: they just count. Facing right counts UP by 1 (clockwise); facing left,
	// up and down count DOWN by 1 (counter-clockwise). They only differ in where they start.
	public static readonly int[][] SwordDirection =
	{
		new[] { 1, 2, 3, 4, 5, 6, 7, 0 }, // facing right: RB, B, LB, L, LT, T, RT, R   (clockwise)
		new[] { 3, 2, 1, 0, 7, 6, 5, 4 }, // facing left:  LB, B, RB, R, RT, T, LT, L   (counter-clockwise)
		new[] { 5, 4, 3, 2, 1, 0, 7, 6 }, // facing up:    LT, L, LB, B, RB, R, RT, T   (counter-clockwise)
		new[] { 1, 0, 7, 6, 5, 4, 3, 2 }, // facing down:  RB, R, RT, T, LT, L, LB, B   (counter-clockwise)
	};

	// Which way Link's BODY faces on each step: 0 = right, 1 = left, 2 = up, 3 = down.
	// Each way is held for two steps, so the body turns a quarter turn every other step.
	public static readonly int[][] BodyDirection =
	{
		new[] { 0, 3, 3, 1, 1, 2, 2, 0 }, // facing right: R, D, D, L, L, U, U, R
		new[] { 1, 3, 3, 0, 0, 2, 2, 1 }, // facing left:  L, D, D, R, R, U, U, L
		new[] { 2, 1, 1, 3, 3, 0, 0, 2 }, // facing up:    U, L, L, D, D, R, R, U
		new[] { 3, 0, 0, 2, 2, 1, 1, 3 }, // facing down:  D, R, R, U, U, L, L, D
	};

	// Frame numbers in the "spin-attack" body animation: down, right, up, left are at
	// frames 0, 1, 3, 5 (the other frames in it are duplicates of these).
	// Indexed by body direction (right, left, up, down).
	private static readonly int[] BodyFrameFor = { 1, 5, 3, 0 };
	public static int BodyFrame(int bodyDirection) => BodyFrameFor[bodyDirection];

	// Spin timing, from UpdateSpinAttackAnimation.
	public const int StartCounter = 32; // $20 (USING_SPIN_ATTACK_MAX)

	// Where the tile check looks (for bushes and grass) on each sword direction, in pixels from
	// the top-left of Link's 16x16 sprite. From SwordCollisionMapX/Y in bank0.asm, the "spin attack" half.
	public static readonly Vector2[] TileProbe =
	{
		new Vector2(22, 8),   // right
		new Vector2(22, 22),  // right-bottom
		new Vector2(8, 22),   // bottom
		new Vector2(-6, 22),  // left-bottom
		new Vector2(-6, 8),   // left
		new Vector2(-6, -6),  // left-top
		new Vector2(8, -6),   // top
		new Vector2(22, -6),  // right-top
	};
}
