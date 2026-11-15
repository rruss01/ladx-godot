using Godot;

public partial class Player : CharacterBody2D
{
	[Signal] public delegate void HealthChangedEventHandler(int health, int maxHealth);
	[Signal] public delegate void RupeesChangedEventHandler(int amount);

	// PLAYER SHOULD NOT BE HANDLING THIS
	private int _health;
	private int _rupees;

	// health mgmt; should emit as signal
	public void TakeDamage(int amount) => SetHealth(_health - amount);
	public void Heal(int amount) => SetHealth(_health + amount);

	// debug value
	[Export] public int MaxHealth { get; set; } = 6;

	// --- Movement (1/16 px per tick; values from LADX disassembly) ---
	private const int StraightSpeed = 16; // $10
	private const int DiagonalSpeed = 12; // $0C per axis
	private Vector2I _facing = Vector2I.Down;
	// Y starts at 8 so the axes don't pause on the same frame (fixes diagonal jitter).
	private Vector2I _subpixel = new Vector2I(0, 8);

	// --- Sword ---
	// Arrays can't be const in C#, so use static readonly.
	private static readonly int[] SwordSwingSteps = [3, 3, 8]; // draw, swing start, swing middle
	private const int SwordSwingFrames = 14; // sum of SwordSwingSteps
	private const int SpinAtkMaxCharge = 40; // $28 frames to fully charge
	private const int SwordReach = 12;
	private int _swingFrame = -1; // -1 = not swinging
	private int _charge = 0;

	private AnimatedSprite2D _sprite;
	private bool _swordOut;
	private SwordSprite _sword;

	public Vector2I Facing => _facing;

	private static readonly ItemButton[] Buttons = { ItemButton.B, ItemButton.A };
	private static string ActionFor(ItemButton b) => b == ItemButton.B ? "button_b" : "button_a";
	private ItemButton _useButton;
	private ItemButton _swordButton;

	// dpad struct to handle movement
	private readonly record struct DPad(bool Right, bool Left, bool Up, bool Down)
	{
		public int X => B(Right) - B(Left);
		public int Y => B(Down) - B(Up);
		public int Count => B(Right) + B(Left) + B(Up) + B(Down);
		public bool Opposing => (Right && Left) || (Up && Down);

		private static int B(bool value) => value ? 1 : 0;
	}

	public override void _Ready()
	{
		_sprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
		GlobalPosition = GlobalPosition.Round();
		CallDeferred(MethodName.EquipStartingItems);
		_sword = GetNode<SwordSprite>("Sword");
	}
	
	private void EquipStartingItems()
	{
		SetHealth(MaxHealth);
		AddRupees(0);
	}

	private void SetHealth(int value)
	{
		_health = Mathf.Clamp(value, 0, MaxHealth);
		EmitSignal(SignalName.HealthChanged, _health, MaxHealth);
	}

	public void AddRupees(int amount)
	{
		_rupees = Mathf.Clamp(_rupees + amount, 0, 999);
		EmitSignal(SignalName.RupeesChanged, _rupees);
	}

	public override void _PhysicsProcess(double delta)
	{
		// 1. Swing in progress: movement fully blocked, only the swing runs
		if (_swingFrame >= 0)
		{
			UpdateSwing();
			return;
		}

		// 2. Use item if item button pressed.
		foreach (ItemButton button in Buttons) {
			if (!Input.IsActionJustPressed(ActionFor(button)))
				continue;

			_useButton = button;
			Inventory.Instance.GetEquipped(button)?.Use(this);

			if (_swingFrame >= 0)
				return;
		}

		// 3. Charging spin attack (swords only)
		if (_swordOut && Input.IsActionPressed(ActionFor(_swordButton)))
		{
			_charge = Mathf.Min(_charge + 1, SpinAtkMaxCharge);
		}
		else
		{
			if (_charge >= SpinAtkMaxCharge) 
			{
				// TODO: spin attack
			}
			_charge = 0;
			_swordOut = false;
			_sword.HideSword();
		}

		// otherwise move normally
		UpdateMovement();
	}

	private DPad ReadDpad()
	{
		return new DPad(
			Right: Input.IsActionPressed("ui_right"),
			Left: Input.IsActionPressed("ui_left"),
			Up: Input.IsActionPressed("ui_up"),
			Down: Input.IsActionPressed("ui_down")
		);
	}

	// Facing only changes when exactly one direction is held (otherwise it's kept)
	private void UpdateFacing(DPad dpad)
	{
		if (dpad.Count == 1 && !_swordOut)
			_facing = new Vector2I(dpad.X, dpad.Y);
	}

	private string FacingDirection()
	{
		if (_facing.X != 0)
			return "right";
		return _facing.Y < 0 ? "up" : "down";
	}

	private void UpdateMovement()
	{
		DPad dpad = ReadDpad();

		// Speed in 1/16 px for this tick (opposing keys cancel everything)
		Vector2I speed = Vector2I.Zero;
		if (!dpad.Opposing)
		{
			int s = (dpad.X != 0 && dpad.Y != 0) ? DiagonalSpeed : StraightSpeed;
			speed = new Vector2I(dpad.X * s, dpad.Y * s);
		}

		// accumulate, then split into whole pixels + leftover fraction
		_subpixel += speed;
		Vector2I whole = new Vector2I(_subpixel.X >> 4, _subpixel.Y >> 4);
		_subpixel = new Vector2I(_subpixel.X & 15, _subpixel.Y & 15);

		// mimics LADX wall slide
		if (whole.X != 0)
			MoveAndCollide(new Vector2(whole.X, 0));
		if (whole.Y != 0)
			MoveAndCollide(new Vector2(0, whole.Y));

		UpdateFacing(dpad);

		if (_swordOut) {
			_sword.ShowPose(_facing, _sword.HoldPose);
			switch (FacingDirection()) {
				case "up":
					_sword.Position += new Vector2(0, 3);
					break;
				case "down":
					_sword.Position -= new Vector2(0, 3);
					break;
				case "right":
					_sword.Position += new Vector2(_facing.X < 0 ? 3 : -3, 0);
					break;
			}
		}

		// Walking animation = any d-pad direction held, even if blocked or cancelled
		string state = dpad.Count > 0 ? "walk" : "idle";
		_sprite.Play($"{state}-{FacingDirection()}");
		_sprite.FlipH = _facing.X < 0;
	}

	public void StartSwing(bool chargeable = true)
	{
		_swordButton = _useButton;
		_swordOut = chargeable;
		UpdateFacing(ReadDpad());
		Velocity = Vector2.Zero;
		_charge = 0;
		_swingFrame = 0;
		UpdateSwing();
	}

	private void UpdateSwing()
	{
		int pose = 0;
		if (_swingFrame >= SwordSwingSteps[0] + SwordSwingSteps[1])
			pose = 2;
		else if (_swingFrame >= SwordSwingSteps[0])
			pose = 1;

		_sprite.Animation = "attack-" + FacingDirection();
		_sprite.Frame = pose;
		_sprite.Pause();
		_sprite.FlipH = _facing.X < 0;
		_sword.ShowPose(_facing, pose);
		if (pose >= 1)
			ProbeSword();

		_swingFrame++;
		if (_swingFrame >= SwordSwingFrames) {
			_swingFrame = -1;
			if (!_swordOut)
				_sword.HideSword();
		}
	}

	private void ProbeSword() {
		Vector2 forward = new Vector2(_facing.X, _facing.Y);
		Vector2 side = new Vector2(Mathf.Abs(forward.Y), Mathf.Abs(forward.X));

		// Three points across the blade, so a slash can hit a tile on either side.
		foreach (float spread in new[] { -7f, 0f, 7f })
			InteractiveTiles.Instance?.TryInteract(GlobalPosition + forward * SwordReach + side * spread, Interaction.Slash);
	}
}
