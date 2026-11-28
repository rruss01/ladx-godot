using Godot;

// TRANSCIENT_VFX_POOF: two 16x16 poses, 7 ticks then 8, 15 ticks total.
public partial class Poof : Node2D
{
	private const int FirstPoseTicks = 7;
	private const int TotalTicks = 15;

	private AnimatedSprite2D _sprite;
	private int _tick;

	public override void _Ready()
	{
		_sprite = GetNode<AnimatedSprite2D>("Sprite");
		_sprite.Animation = "default";
		_sprite.Pause();
		_sprite.Frame = 0;
		_tick = 0;
	}

	public override void _PhysicsProcess(double delta)
	{
		_tick++;
		if (_tick >= TotalTicks) { QueueFree(); return; }
		_sprite.Frame = _tick < FirstPoseTicks ? 0 : 1;
	}
}
