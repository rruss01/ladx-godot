using Godot;

public partial class WandProjectile : Projectile
{
	private const int FireTicks = 47; // original: private countdown $30, rendered until it reaches 1

	private AnimatedSprite2D _sprite;
	private AnimatedSprite2D Sprite => _sprite ??= GetNode<AnimatedSprite2D>("Sprite");

	public override void _Ready()
	{
		Sprite.Animation = "default";
		Sprite.Frame = 0;
	}
	
	private Vector2 LeadingPoint() {
		Vector2 half = ((RectangleShape2D)Hitbox.Shape).Size / 2f;
		return Hitbox.GlobalPosition + new Vector2(
			Direction.X * (half.X + 1), 
			Direction.Y * (half.Y + 1)
			);
	}

	protected override void OnWallHit()
	{
		if (InteractiveTiles.Instance?.TryInteract(LeadingPoint(), Interaction.Fire) == true)
			return; // runs through bushes, ignores tall grass

		// Anything else: stop, burn in place.
		DyingFrames = FireTicks;
		Velocity = Vector2I.Zero;
		Sprite.Play("flame");
		// Hitbox stays enabled until fire burns out
	}
}
