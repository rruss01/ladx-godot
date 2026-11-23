using Godot;
using System;

public partial class Bomb : CharacterBody2D
{
	
	public const int FuseTicks = 136;
	public const int BlueFrames = 88;
	public const int RedFrames = 38;
	public const int PreExplosionFrames = 10;
	public const int TicksPerFlash = 4;

	private enum Phase { Placed, Flashing, Exploding }
	private Phase _phase = Phase.Placed;
	
	private Vector2I _velocity;             // 1/16 px per tick
	private Vector2I _subpixel = Vector2I.Zero;
	private int _fuse; // frames into the fuse
	private bool _isHeld = false;
	
	private AnimatedSprite2D _sprite;
	
	public static int DirectionIndex(Vector2I facing) =>
		facing.X > 0 ? 0 : facing.X < 0 ? 1 : facing.Y < 0 ? 2 : 3;
	
	private static readonly Vector2[] BombOffset = {
		new Vector2(8, 0), // right
		new Vector2(-8, 0), // left
		new Vector2(0, -3), // up
		new Vector2(0, 4) // down
	};
	
	private void StartFuse() {
		_fuse = FuseTicks;
	}
	
	private void UpdateFuse() {
		if (_fuse > 0)	
			_fuse--;
		
		if (_fuse > 48) {
			_phase = Phase.Placed;
			_sprite.Frame = 0;
		} else if (_fuse > 10) {
			_phase = Phase.Flashing;
			_sprite.Frame = (_fuse / TicksPerFlash) % 2;
		} else {
			_phase = Phase.Exploding;
			_sprite.Frame = 2; // pre-explode frame
		}
	}
	
	public void Place(Player player) {
		GlobalPosition = player.GlobalPosition + BombOffset[DirectionIndex(player.Facing)];
	}
	
	public override void _Ready()
	{
		StartFuse();
		
		_sprite = GetNode<AnimatedSprite2D>("Sprite");
		_sprite.Animation = "bomb";
		_sprite.Pause(); // we pick the frame ourselves so it stays in step with the physics
	}
	
	public override void _PhysicsProcess(double delta) {
		if (!_isHeld)
			UpdateFuse();
	}
}
