using Godot;

public partial class Explosion : Node2D
{
	[Export] public AudioStream ExplosionSound { get; set; }
	public const int ExplosionFrames = 24;
	private const int Cell = 16; // tile size

	private static readonly Vector2I[] CheckOrder =
	{
		new( 1,  1), new( 0,  1), new(-1,  1),
		new( 1,  0), new( 0,  0), new(-1,  0),
		new( 1, -1), new( 0, -1), new(-1, -1)
	};

	private int _counter = 0;
	private AnimatedSprite2D _sprite;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_counter = 0;
		_sprite = GetNode<AnimatedSprite2D>("Sprite");
		_sprite.Animation = "default";
		PlayExplosionSound();
	}
	
	private void PlayExplosionSound()
	{
		AudioStreamPlayer sound = new AudioStreamPlayer { Stream = ExplosionSound };
		sound.Finished += sound.QueueFree;
		GetParent().AddChild(sound);
		sound.Play();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (_counter >= 1 && _counter <= CheckOrder.Length)
			BreakTile(CheckOrder[_counter - 1]);

		_counter++;
		
		if (_counter < 7) {
			_sprite.Frame = 0;
		} else if (_counter < 15) {
			_sprite.Frame = 1;
		} else { 
			_sprite.Frame = 2 ;
			if (_counter >= ExplosionFrames)
				QueueFree();
		}
	}

	private void BreakTile(Vector2I offset)
	{
		Vector2I center = new Vector2I(
			Mathf.FloorToInt(GlobalPosition.X / Cell),
			Mathf.FloorToInt(GlobalPosition.Y / Cell));

		Vector2 point = new(
			(center.X + offset.X) * Cell + Cell / 2f,
			(center.Y + offset.Y) * Cell + Cell / 2f);

		InteractiveTiles.Instance?.TryInteract(point, Interaction.Bomb);
	}
}
