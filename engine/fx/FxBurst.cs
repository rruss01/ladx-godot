using Godot;

// Base class for short effects made of hand-placed sprite poses, the way the original game
// does them ("sprite rect" data): a few poses of a few pieces each, every pose shown for a
// fixed number of game frames. Each piece offset is the piece's TOP-LEFT corner relative to
// this node's origin, which callers place at the top-left of the 16x16 object that broke.
// Counted in physics ticks (60 per second); positions are always whole pixels.
public abstract partial class FxBurst : Node2D
{
	public Texture2D PieceTexture { get; set; }   // null = the subclass's placeholder
	public int TicksPerFrame { get; set; } = 4;   // how long each pose is shown
	public int TotalTicks { get; set; } = 30;     // when the effect removes itself

	protected readonly record struct Piece(int X, int Y, bool FlipX, bool FlipY);

	protected abstract Piece[][] Poses { get; }

	// Which pose to show on a given tick. Subclasses can change the order.
	protected virtual int PoseAt(int tick) => Mathf.Min(tick / TicksPerFrame, Poses.Length - 1);

	private Sprite2D[] _pieces;
	private int _tick;

	public override void _Ready()
	{
		Texture2D texture = PieceTexture;
		_pieces = new Sprite2D[Poses[0].Length];

		for (int i = 0; i < _pieces.Length; i++)
		{
			_pieces[i] = new Sprite2D
			{
				Texture = texture,
				Centered = false, // position = top-left corner, like an OAM sprite
				TextureFilter = TextureFilterEnum.Nearest,
			};
			AddChild(_pieces[i]);
		}

		ShowPose(PoseAt(0));
	}

	public override void _PhysicsProcess(double delta)
	{
		_tick++;
		if (_tick >= TotalTicks)
		{
			QueueFree();
			return;
		}

		ShowPose(PoseAt(_tick));
	}

	private void ShowPose(int pose)
	{
		for (int i = 0; i < _pieces.Length; i++)
		{
			Piece piece = Poses[pose][i];
			_pieces[i].Position = new Vector2(piece.X, piece.Y);
			_pieces[i].FlipH = piece.FlipX;
			_pieces[i].FlipV = piece.FlipY;
		}
	}
}
