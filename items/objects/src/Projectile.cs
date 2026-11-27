using Godot;
using System;

public partial class Projectile : Area2D
{
	public enum Side { Player, Enemy } // affiliation

	[Export] public uint WallMask { get; set; } = 1;
	[Export] public Side Affiliation { get; set; } = Side.Player;
	[Export] public int Damage { get; set; } = 2;
	public int Speed = 32;

	protected Vector2I Direction;
	protected Vector2I Velocity;
	private Vector2I _subPos;
	private CollisionShape2D _hitbox;
	protected CollisionShape2D Hitbox => _hitbox ??= GetNode<CollisionShape2D>("Hitbox");

	protected int DyingFrames = -1;
	protected virtual int BounceShift => 3;

	private readonly PhysicsShapeQueryParameters2D _query = new()
	{
		CollideWithBodies = true,
		CollideWithAreas = false
	};

	public virtual void Launch(Vector2 origin, Vector2I direction)
	{
		Direction = direction;
		Velocity = direction * Speed;
		GlobalPosition = origin;
		_subPos = new Vector2I((int)origin.X, (int)origin.Y) * 16;
		AddToGroup("projectile");
	}
	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _PhysicsProcess(double delta)
	{
		if (DyingFrames >= 0) { UpdateDying(); return; }

		_subPos += Velocity;
		GlobalPosition = new Vector2(_subPos.X >> 4, _subPos.Y >> 4);

		if (HitsWall()) { OnWallHit(); return; }
		if (IsOutsideRoom()) { QueueFree(); return; }
		CheckTargets();
	}

	private bool IsOutsideRoom()
	{
		Camera2D cam = GetViewport().GetCamera2D();
		if (cam == null) return false;

		Vector2 size = cam is ScreenScroller scroller ? scroller.RoomSize : GetViewportRect().Size;
		Rect2 room = new Rect2(cam.GlobalPosition, size);
		return !room.HasPoint(GlobalPosition);
	}

	private bool HitsWall()
	{
		_query.Shape = Hitbox.Shape;
		_query.Transform = Hitbox.GlobalTransform;
		_query.CollisionMask = WallMask;

		var hits = GetWorld2D().DirectSpaceState.IntersectShape(_query);
		foreach (var hit in hits)
			if (hit["collider"].AsGodotObject() is TileMapLayer)
				return true;
		return false;
	}

	protected void Bounce()
	{
		Velocity = new Vector2I((-Velocity.X) >> BounceShift, (-Velocity.Y) >> BounceShift);
	}

	private void CheckTargets() {}

	private void UpdateDying()
	{
		DyingFrames--;
		if (DyingFrames < 0) { QueueFree(); return; }
		_subPos += Velocity;
		GlobalPosition = new Vector2(_subPos.X >> 4, _subPos.Y >> 4); 
		OnDyingFrame();
	}

	protected virtual void OnWallHit() { DyingFrames = 24; Bounce(); }
	protected virtual void OnDyingFrame() {}
	protected virtual void OnHitTarget(Node target) {}
}
