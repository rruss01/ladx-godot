using Godot;

// How the original works (LADX-Disassembly, src/code/entities/19_boomerang.asm):
//   * Thrown at 2 px/tick straight, or 1.5 px/tick per axis diagonally.
//   * Flies OUT for 40 ticks ($28), or until it hits a wall, whichever comes first.
//   * Then it turns around: it crawls at 0.5 px/tick for a moment, and every 4th tick it
//     re-aims at Link's CURRENT position at "speed 32" (the longer axis moves 2 px/tick).
//   * On the way back it ignores walls. It disappears when it touches Link.
public partial class Boomerang : CharacterBody2D
{
	public int OutboundTicks { get; set; } = 40;     // $28
	public int ReturnStartSpeed { get; set; } = 8;   // speed on the turnaround frame
	public int ReturnSpeed { get; set; } = 32;       // $20, re-aimed at player...
	public int SteerEveryTicks { get; set; } = 4;    // ...every 4 frames
	public int SoundEveryTicks { get; set; } = 26;   // $1A: the whoosh restarts this often
	public int TicksPerSpinFrame { get; set; } = 4;  // sprite changes every 4 frames
	public float CatchDistance { get; set; } = 8f;   // pixels from Link's origin

	// Played when the boomerang hits a wall (the original uses the sword "poking" jingle).
	[Export] public AudioStream ClinkSound { get; set; }

	// Who to fly back to. Set by Launch().
	public Node2D Thrower { get; private set; }

	private enum Phase { Thrown, Returning }

	private Phase _phase = Phase.Thrown;
	private Vector2I _velocity;             // 1/16 px per tick
	private Vector2I _subpixel = Vector2I.Zero;
	private int _ticksLeft;                 // outbound ticks remaining
	private int _age;
	private bool _hitPlainWall;             // this tick's hit included a wall that isn't an interactive tile

	private AnimatedSprite2D _sprite;
	private AudioStreamPlayer _sound;

	// Call before adding the boomerang to the tree.
	public void Launch(Node2D thrower, Vector2I velocity16)
	{
		Thrower = thrower;
		_velocity = velocity16;

		// The boomerang spawns on top of Link. Without this, its first step into Link's own
		// collision shape (e.g. when thrown down) counts as a wall hit.
		if (thrower is PhysicsBody2D body)
			AddCollisionExceptionWith(body);
	}

	public override void _Ready()
	{
		_ticksLeft = OutboundTicks;

		_sprite = GetNode<AnimatedSprite2D>("Sprite");
		_sound = GetNode<AudioStreamPlayer>("Sound");

		if (_sprite != null)
		{
			_sprite.Animation = "default";
			_sprite.Pause(); // we pick the frame ourselves so it stays in step with the physics
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		// fake loop
		if (SoundEveryTicks > 0 && _age % SoundEveryTicks == 0)
			_sound?.Play();

		if (_sprite != null && _sprite.SpriteFrames != null)
		{
			int frames = _sprite.SpriteFrames.GetFrameCount(_sprite.Animation);
			if (frames > 0)
				_sprite.Frame = (_age / TicksPerSpinFrame) % frames;
		}

		_age++;
		if (_ticksLeft > 0)
			_ticksLeft--;

		// Order matches the original: move first, then run the state logic.
		bool hitWall = Move();

		if (_phase == Phase.Thrown)
			UpdateThrown(hitWall);
		else
			UpdateReturning();
	}

	private void UpdateThrown(bool hitWall)
	{
		if (_ticksLeft == 0)
		{
			// Out of range, or it hit something on the previous tick: turn around.
			_velocity = VectorTowardsThrower(ReturnStartSpeed);
			_phase = Phase.Returning;
			return;
		}

		if (hitWall) // TODO: or leaves screen bounds
		{
			// Cut the outbound timer; the turn-around happens next tick.
			_ticksLeft = 0;

			// Only a plain wall clinks; bouncing off a bush or other solid interactive tile is quiet.
			if (_hitPlainWall)
				PlayClink();
		}
	}

	private void UpdateReturning()
	{
		if (!IsInstanceValid(Thrower))
		{
			QueueFree();
			return;
		}

		if (_age % SteerEveryTicks == 0)
			_velocity = VectorTowardsThrower(ReturnSpeed);

		if (GlobalPosition.DistanceTo(Thrower.GlobalPosition) <= CatchDistance)
			QueueFree(); // caught
	}

	// Moves by this tick's velocity. Returns true if a wall stopped it (outbound only).
	private bool Move()
	{
		// Same subpixel trick as Player.cs: accumulate 1/16 px, move by whole pixels.
		_subpixel += _velocity;
		Vector2I whole = new Vector2I(_subpixel.X >> 4, _subpixel.Y >> 4);
		_subpixel = new Vector2I(_subpixel.X & 15, _subpixel.Y & 15);

		if (_phase == Phase.Returning)
		{
			// The returning boomerang flies straight through walls...
			GlobalPosition += new Vector2(whole.X, whole.Y);

			// ...but still cuts any bushes/grass it passes over.
			CutTileUnderneath();
			return false;
		}

		_hitPlainWall = false;
		bool hit = false;
		if (whole.X != 0)
			hit |= Step(new Vector2(whole.X, 0));
		if (whole.Y != 0)
			hit |= Step(new Vector2(0, whole.Y));

		// Tall grass has no collision
		CutTileUnderneath();
		return hit;
	}

	private bool Step(Vector2 motion)
	{
		KinematicCollision2D collision = MoveAndCollide(motion);
		if (collision == null)
			return false;

		// Probe point just inside the tile it ran into
		Vector2 insideWall = collision.GetPosition() - collision.GetNormal() * 2f;
		InteractiveTiles tiles = InteractiveTiles.Instance;

		// Ask BEFORE slashing: a cut bush turns inert and would look like a plain wall.
		if (tiles != null && tiles.IsInteractiveAt(insideWall))
		{
			tiles.TryInteract(insideWall, Interaction.Slash, 0, false);
		}
		else
		{
			_hitPlainWall = true;
		}

		return true;
	}

	// Cuts whatever slashable tile is under the boomerang's center
	private void CutTileUnderneath()
	{
		InteractiveTiles.Instance?.TryInteract(GlobalPosition, Interaction.Slash, 0, false);
	}

	// The LONGER axis gets exactly 'length', the shorter axis gets the proportional share. 
	// So a diagonal return is faster than a straight one.
	private Vector2I VectorTowardsThrower(int length)
	{
		if (!IsInstanceValid(Thrower))
			return Vector2I.Zero;

		Vector2 d = Thrower.GlobalPosition - GlobalPosition;
		int dx = Mathf.RoundToInt(d.X);
		int dy = Mathf.RoundToInt(d.Y);
		int ax = Mathf.Abs(dx);
		int ay = Mathf.Abs(dy);
		if (ax == 0 && ay == 0)
			return Vector2I.Zero;

		int major = Mathf.Max(ax, ay);
		int minor = Mathf.Min(ax, ay);
		int minorSpeed = length * minor / major; // integer division rounds down

		int sx = (ax >= ay ? length : minorSpeed) * Mathf.Sign(dx);
		int sy = (ax >= ay ? minorSpeed : length) * Mathf.Sign(dy);
		return new Vector2I(sx, sy);
	}

	private void PlayClink()
	{
		AudioStreamPlayer clink = new AudioStreamPlayer { Stream = ClinkSound };
		clink.Finished += clink.QueueFree;
		GetParent().AddChild(clink);
		clink.Play();
	}
}
