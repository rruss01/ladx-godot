using Godot;

public enum DropKind { Heart, Rupee }

// A heart or rupee lying on the ground after a bush is cut. One scene per kind:
//
//   HeartDrop   (Area2D, this script, Kind = Heart, Amount = 2)
//   |- CollisionShape2D   (small, about 8x8)
//   |- Sprite             (Sprite2D named "Sprite": your art, centred on the node)
//
// The Area2D's collision MASK must include the layer Link is on (layer/monitoring as in ItemPickup).
// All numbers below come from LADX-Disassembly (DroppableHeart/RupeeEntityHandler, PickableHandler).
public partial class Droppable : Area2D
{
	[Export] public DropKind Kind { get; set; } = DropKind.Heart;

	// Heart: in HALF-hearts. The original gives 8 of the 8 units a heart is made of = 1 heart = 2.
	// Rupee: 1.
	[Export] public int Amount { get; set; } = 2;

	// Original: JINGLE_GOT_HEART for hearts, WAVE_SFX_SEASHELL for rupees.
	[Export] public AudioStream PickupSound { get; set; }

	// --- The original's timings (60 ticks per second) ---
	private const int HopSpeed = 16;          // $10: starting upward speed, 1/16 px per tick
	private const int GravitySpeed = 2;            // speed drops by 2 every tick
	private const int MinBounceSpeed = 7;     // a bounce slower than this and it just rests
	private const int PickupDelayTicks = 24;  // $18: can't be grabbed right away
	private const int LifetimeSteps = 128;    // $80: counts down once every 4 ticks (about 8.5 s)
	private const int BlinkBelowStep = 28;    // $1C: blinks for the last 28 steps (about 1.9 s)

	private Node2D _sprite;
	private int _age;
	private int _z16;                         // height above the ground, in 1/16 px
	private int _speedZ = HopSpeed;
	private bool _resting;

	public override void _Ready()
	{
		AddToGroup("droppable"); // ItemDrops clears these when the screen scrolls
		_sprite = GetNodeOrNull<Node2D>("Sprite");
	}

	public override void _PhysicsProcess(double delta)
	{
		_age++;

		UpdateHop();
		UpdateLifetime();

		if (_age >= PickupDelayTicks)
			TryCollect();
	}

	// Same order as the original: add speed to height, then apply gravity, then check the ground.
	private void UpdateHop()
	{
		if (_resting)
			return;

		_z16 += _speedZ;
		_speedZ -= GravitySpeed;

		if (_z16 < 0)
		{
			_z16 = 0;

			// Bounce at half the landing speed (the original's "sra, cpl").
			int bounce = ~(_speedZ >> 1);
			if (bounce >= MinBounceSpeed)
			{
				_speedZ = bounce;
			}
			else
			{
				_speedZ = 0;
				_resting = true;
			}
		}

		if (_sprite != null)
			_sprite.Position = new Vector2(0, -(_z16 >> 4));
	}

	private void UpdateLifetime()
	{
		int step = LifetimeSteps - _age / 4;

		if (step <= 0)
		{
			QueueFree();
			return;
		}

		// Over the last stretch it flickers: hidden on even steps, shown on odd ones.
		if (_sprite != null)
			_sprite.Visible = step >= BlinkBelowStep || (step & 1) == 1;
	}

	// Polls instead of using BodyEntered, because Link may already be standing on it
	// when the pickup delay ends.
	private void TryCollect()
	{
		foreach (Node2D body in GetOverlappingBodies())
		{
			if (body is Player player)
			{
				Collect(player);
				return;
			}
		}
	}

	private void Collect(Player player)
	{
		switch (Kind)
		{
			case DropKind.Heart:
				player.AddHealth(Amount);
				break;
			case DropKind.Rupee:
				player.AddRupees(Amount);
				break;
		}

		PlayPickupSound();
		QueueFree();
	}

	private void PlayPickupSound()
	{
		if (PickupSound == null)
			return;

		// Parent it to our parent so the sound isn't cut off when this node is freed.
		AudioStreamPlayer sound = new AudioStreamPlayer { Stream = PickupSound };
		sound.Finished += sound.QueueFree;
		GetParent().AddChild(sound);
		sound.Play();
	}
}
