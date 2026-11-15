using Godot;

// The camera sits still on a fixed grid of "rooms". When the target's body
// crosses a room edge, the whole tree is paused and the camera scrolls in
// fixed pixel steps to the neighboring room. Then the game resumes.
public partial class ScreenScroller : Camera2D
{
	[Signal] public delegate void RoomChangedEventHandler(Vector2I room);

	[Export] public Node2D Target { get; set; }

	public Vector2I RoomSize { get; set; } = new Vector2I(160, 128);

	// TODO: Change collision area to Player hitbox?
	public Vector2I TargetHalfSize { get; set; } = new Vector2I(8, 8);

	// Pixels the camera moves per frame during a scroll.
	public int ScrollSpeed { get; set; } = 4;

	// Distance from the new room's edge to the target's CENTER after a scroll.
	// Keep this >= TargetHalfSize, or the target lands partly outside the room
	// and immediately triggers another scroll.
	public int EdgeInset { get; set; } = 12;

	// Rooms outside this grid range are ignored (no scrolling into the void).
	public Vector2I MinRoom { get; set; } = Vector2I.Zero;
	public Vector2I MaxRoom { get; set; } = new Vector2I(15, 15);

	public Vector2I CurrentRoom => _room;

	private Vector2I _room;
	private Vector2I _nextRoom;
	private bool _scrolling;
	private int _ticks;
	private int _totalTicks;
	private Vector2 _camFrom, _camTo;
	private Vector2 _targetFrom, _targetTo;

	public override void _Ready()
	{
		// Keep running while the rest of the game is paused; needed for HUD as well.
		ProcessMode = ProcessModeEnum.Always;

		// Position now means "top-left corner of the view", so room origins line up.
		AnchorMode = AnchorModeEnum.FixedTopLeft;
		PositionSmoothingEnabled = false;
		Warp(7, 8);
		SnapToTarget();
	}

	// Jump straight to the room containing the target, with no scroll.
	// Call this after warps, stairs, or spawns/respawns.
	public void SnapToTarget()
	{
		_room = RoomOf(Target.GlobalPosition);
		GlobalPosition = RoomOrigin(_room);
		EmitSignal(SignalName.RoomChanged, _room);
	}

	public void Warp(int roomX, int roomY, int? screenX = null, int? screenY = null) {
		if (_scrolling)
			return;

		Vector2I room = new Vector2I(roomX, roomY);
		if(!InBounds(room)) {
			GD.PushWarning("Out of bounds. Cancelling warp.");
			return;
		}

		Vector2 current = Target.GlobalPosition - RoomOrigin(_room);

		Vector2 local = new Vector2(Mathf.Clamp(screenX ?? current.X, TargetHalfSize.X, RoomSize.X - TargetHalfSize.X), 
			Mathf.Clamp(screenY ?? current.Y, TargetHalfSize.Y, RoomSize.Y - TargetHalfSize.Y));

		Target.GlobalPosition = (RoomOrigin(room) + local).Round();
		SnapToTarget();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_scrolling)
		{
			StepScroll();
			return;
		}

		Vector2I dir = CrossedDirection(Target.GlobalPosition);

		// One axis at a time; if both are crossed, horizontal goes first
		if (dir.X != 0)
			dir.Y = 0;

		if (dir != Vector2I.Zero && InBounds(_room + dir))
			StartScroll(dir);
	}

	// Which way has the target's body poked past the current room's edge?
	// Uses velocity, NOT facing direction
	private Vector2I CrossedDirection(Vector2 p)
	{
		Vector2 origin = RoomOrigin(_room);
		Vector2I dir = Vector2I.Zero;

		if (p.X - TargetHalfSize.X < origin.X)
			dir.X = -1;
		else if (p.X + TargetHalfSize.X > origin.X + RoomSize.X)
			dir.X = 1;

		if (p.Y - TargetHalfSize.Y < origin.Y)
			dir.Y = -1;
		else if (p.Y + TargetHalfSize.Y > origin.Y + RoomSize.Y)
			dir.Y = 1;

		return dir;
	}

	private void StartScroll(Vector2I dir)
	{
		_nextRoom = _room + dir;
		_camFrom = GlobalPosition;
		_camTo = RoomOrigin(_nextRoom);

		// Move the target into the new room (only on the crossing axis).
		Vector2 origin = _camTo;
		Vector2 p = Target.GlobalPosition;
		_targetFrom = p;
		_targetTo = p;
		if (dir.X != 0)
			_targetTo.X = Mathf.Clamp(p.X, origin.X + EdgeInset, origin.X + RoomSize.X - EdgeInset);
		if (dir.Y != 0)
			_targetTo.Y = Mathf.Clamp(p.Y, origin.Y + EdgeInset, origin.Y + RoomSize.Y - EdgeInset);

		int distance = dir.X != 0 ? RoomSize.X : RoomSize.Y;
		_totalTicks = Mathf.Max(1, Mathf.CeilToInt((float)distance / ScrollSpeed));
		_ticks = 0;
		_scrolling = true;

		GetTree().Paused = true; // freezes the player, enemies, everything but this camera
	}

	private void StepScroll()
	{
		_ticks++;
		float t = (float)_ticks / _totalTicks;

		// Round so both camera and target always land on whole pixels.
		GlobalPosition = _camFrom.Lerp(_camTo, t).Round();
		Target.GlobalPosition = _targetFrom.Lerp(_targetTo, t).Round();

		if (_ticks >= _totalTicks)
		{
			_room = _nextRoom;
			_scrolling = false;
			EmitSignal(SignalName.RoomChanged, _room); // update listeners
			GetTree().Paused = false;                  // then resume game
		}
	}

	private Vector2I RoomOf(Vector2 worldPos)
	{
		// FloorToInt so negative coordinates land in the right room.
		return new Vector2I(
			Mathf.FloorToInt(worldPos.X / RoomSize.X),
			Mathf.FloorToInt(worldPos.Y / RoomSize.Y));
	}

	private Vector2 RoomOrigin(Vector2I room)
	{
		return new Vector2(room.X * RoomSize.X, room.Y * RoomSize.Y);
	}

	private bool InBounds(Vector2I room)
	{
		return room.X >= MinRoom.X && room.X <= MaxRoom.X
			&& room.Y >= MinRoom.Y && room.Y <= MaxRoom.Y;
	}
}
