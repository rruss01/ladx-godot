using Godot;

public partial class Arrow : Projectile
{
	private AnimatedSprite2D _sprite;
	private AnimatedSprite2D Sprite => _sprite ??= GetNode<AnimatedSprite2D>("Sprite");
	private Vector2 _spriteBase;
	private int _z, _vz;

	protected override int BounceShift => 2;

	private static readonly Vector2I[] SpinFrames =
		{ Vector2I.Right, Vector2I.Down, Vector2I.Left, Vector2I.Up };

	public override void _Ready()
	{
		Sprite.Animation = "default";
		Sprite.Pause();
		_spriteBase = Sprite.Position;
	}

	private static Vector2 ArrowTipOffset(Vector2I dir) => dir switch
	{
		{ X: > 0 } => new Vector2(5, 0),
		{ X: < 0 } => new Vector2(-5, 0),
		{ Y: < 0 } => new Vector2(0, -5),
		_ => new Vector2(0, 5)
	};

	private void ShowFacing(Vector2I dir)
	{
		Sprite.Frame = dir.X != 0 ? 1 : 0;
		Sprite.FlipH = dir.X < 0;
		Sprite.FlipV = dir.Y < 0;
	}

	public override void Launch(Vector2 origin, Vector2I direction)
	{
		base.Launch(origin, direction);
		Hitbox.RotationDegrees = direction.X != 0 ? 90 : 0;
		Hitbox.Position = ArrowTipOffset(direction);
		ShowFacing(direction);
	}

	protected override void OnWallHit()
	{
		base.OnWallHit();
		_vz = 16; _z = 0;
		Hitbox.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
	}

	protected override void OnDyingFrame()
	{
		ShowFacing(SpinFrames[(DyingFrames >> 3) & 3]);
		_z += _vz; _vz -= 2;
		Sprite.Position = _spriteBase + new Vector2(0, -(_z >> 4));
	}
}
