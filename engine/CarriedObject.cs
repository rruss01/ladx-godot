using Godot;

public partial class CarriedObject : CharacterBody2D
{
	private enum Phase { Lifting, Carried, Thrown }

	// Where the object is during each stage of the lift, in pixels from the CENTER of Link's
	// sprite: X, Y, and Z (how high above the ground). Row = the way Link faces
	// (right, left, up, down); column = stage 1, 2, 3, then 4 = "over his head".
	private static readonly (int X, int Y, int Z)[][] Stages =
	{
		new[] { (16, 0, 0), (16, 0, 0), (8, 0, 8), (0, 0, 14) },   // right
		new[] { (-16, 0, 0), (-16, 0, 0), (-8, 0, 8), (0, 0, 14) },// left
		new[] { (0, 0, 0), (0, 0, 0), (0, 0, 8), (0, 0, 14) },     // up
		new[] { (-1, 0, 0), (-1, 0, 0), (-1, 8, 0), (-1, 0, 14) }, // down
	};

	private const int ThrowSpeed = 3;   // $30 = 48 sixteenths = 3 px per tick
	private const int ThrowSpeedZ = 4;  // starting upward speed, in 1/16 px per tick
	private const int Gravity = 2;      // speed drops by 2 every tick (same as the heart's hop)

	private Player _player;
	private LiftedTile _tile;
	private int _direction;             // 0 right, 1 left, 2 up, 3 down (Link's facing when lifted)
	private int _stageTicks;            // how long stages 2 and 3 last (8, or 4 with bracelet 2)

	private Phase _phase = Phase.Lifting;
	private int _tick;
	private Vector2 _step;              // thrown: pixels per tick
	private int _z16;                   // thrown: height in 1/16 px
	private int _vz;                    // thrown: vertical speed in 1/16 px per tick

	private Sprite2D _sprite;
	private ScreenScroller _scroller;

	// True once the object is over Link's head and he may walk and throw.
	public bool IsLifted => _phase != Phase.Lifting;

	public static CarriedObject Create(Player player, LiftedTile tile, int direction, int stageTicks)
	{
		return new CarriedObject
		{
			_player = player,
			_tile = tile,
			_direction = direction,
			_stageTicks = stageTicks,
		};
	}

	public override void _Ready()
	{
		ZIndex = 5; // in front of Link

		_sprite = new Sprite2D { Texture = _tile.Art, TextureFilter = TextureFilterEnum.Nearest };
		AddChild(_sprite);

		// only collides once thrown
		AddChild(new CollisionShape2D
		{
			Shape = new RectangleShape2D { Size = new Vector2(8, 5) },
			Position = new Vector2(0, 5),
			Visible = false
		});
		CollisionLayer = 0;
		CollisionMask = 0;

		// The original deletes every entity when the screen changes, so a carried object is lost too.
		_scroller = InteractiveTiles.Instance?.Scroller;
		if (_scroller != null)
			_scroller.RoomChanged += OnRoomChanged;

		FollowPlayer(); // placed immediately so it never shows for a frame at (0, 0)
	}

	public override void _ExitTree()
	{
		if (IsInstanceValid(_scroller))
			_scroller.RoomChanged -= OnRoomChanged;
	}

	private void OnRoomChanged(Vector2I room) => QueueFree();

	public override void _PhysicsProcess(double delta)
	{
		if (_phase == Phase.Thrown)
		{
			UpdateThrown();
			return;
		}

		// Stage 1 lasts one tick, stages 2 and 3 last _stageTicks each, then it's overhead.
		if (_phase == Phase.Lifting && _tick > 2 * _stageTicks)
			_phase = Phase.Carried;

		FollowPlayer();
		_tick++;
	}

	private int Stage()
	{
		if (_tick == 0) return 1;
		if (_tick <= _stageTicks) return 2;
		if (_tick <= 2 * _stageTicks) return 3;
		return 4;
	}

	private void FollowPlayer()
	{
		var (x, y, z) = Stages[_direction][Stage() - 1];
		GlobalPosition = _player.GlobalPosition + new Vector2(x, y);
		SetHeight(z + _player.Height);
	}

	// The sprite is drawn higher up the screen the higher the object is; the node itself
	// (and so its collision shape) stays on the ground.
	private void SetHeight(float pixels)
	{
		_sprite.Position = new Vector2(0, -Mathf.Round(pixels));
	}

	// Called by Player when the button is pressed again. 'facing' is the way Link looks NOW,
	// not the way he faced when he lifted it.
	public void Throw(Vector2I facing)
	{
		_phase = Phase.Thrown;
		_step = new Vector2(facing.X, facing.Y) * ThrowSpeed;
		_z16 = Mathf.RoundToInt(-_sprite.Position.Y * 16f);
		_vz = ThrowSpeedZ;

		// From now on walls stop it, but Link himself never does.
		CollisionMask = _player.CollisionMask;
		AddCollisionExceptionWith(_player);
	}

	private void UpdateThrown()
	{
		// 1. Fly sideways. A wall in the way stops it.
		KinematicCollision2D hit = MoveAndCollide(_step);

		// 2. Rise or fall: add the speed to the height, then gravity lowers the speed.
		_z16 += _vz;
		_vz -= Gravity;
		SetHeight(Mathf.Max(_z16, 0) >> 4);

		// 3. It breaks when it hits a wall or the ground (the original checks both).
		if (hit != null || _z16 <= 0)
			Break();
	}

	private void Break()
	{
		// The effect starts at the top-left corner of the 16x16 object, wherever it is drawn now.
		Vector2 topLeft = GlobalPosition + _sprite.Position - new Vector2(8, 8);
		InteractiveTiles.Instance?.SpawnEffect(_tile.BreakEffect, topLeft.Round());

		// The drop appears where the object broke: the centre of the 16x16 it occupied.
		InteractiveTiles.Instance?.RequestSpawn(_tile.Spawns, (topLeft + new Vector2(8, 8)).Round());
		QueueFree();
	}
}
