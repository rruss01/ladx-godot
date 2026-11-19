using Godot;

public partial class Player : CharacterBody2D
{
	[Signal] public delegate void HealthChangedEventHandler(int health, int maxHealth);
	[Signal] public delegate void RupeesChangedEventHandler(int amount);

	private int _health;
	private int _rupees;

	// health mgmt; should emit as signal
	public void TakeDamage(int amount) => SetHealth(_health - amount);
	public void Heal(int amount) => SetHealth(_health + amount);

	// debug value
	[Export] public int MaxHealth { get; set; } = 6;

	// --- Movement (values from LADX disassembly) ---
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
	public Vector2 SpriteTopLeftOffset { get; set; } = new Vector2(-8, -8);
	public bool ShowSwordHitbox { get; set; } = false;

	// --- Roc's Feather ---
	private const int JumpVelocity = 32; // $20
	private const int Gravity = 2;
	private int _z;
	private int _vz;
	private int _airTicks;
	private Vector2 _spriteBase, _swordBase;
	public bool InAir { get; private set; }
	public float Height => _z / 16f;
	
	private AnimatedSprite2D _sprite;
	private bool _swordOut;
	private SwordSprite _sword;
	private SwordAudio _swordAudio;

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
		_swordAudio = GetNode<SwordAudio>("SwordAudio");
		_spriteBase = _sprite.Offset;
		_swordBase = _sword.Offset;
	}
	
	public void Jump(AudioStream sound = null) {
		if (InAir)
			return;
			
		InAir = true;
		_z = 0;
		_vz = JumpVelocity;
		_airTicks = 0;
		
		if (sound != null) {
			AudioStreamPlayer p = new AudioStreamPlayer { Stream = sound };
			p.Finished += p.QueueFree;
			AddChild(p);
			p.Play();
		}
	}
	
	private void UpdateAir() {
		if (!InAir)
			return;
		
		_airTicks++;
		_z += _vz;
		_vz -= Gravity;
		
		if (_z <= 0) {
			_z = 0;
			InAir = false;
		}
		
		Vector2 lift = new Vector2(0, -Mathf.Round(_z / 16f));
		_sprite.Offset = _spriteBase + lift;
		_sword.Offset = _swordBase + lift;
	}
	
	private void EquipStartingItems()
	{
		SetHealth(6);
		AddRupees(0);
	}
	
	public void AddHealth(int halfHearts) { _health = Mathf.Min(_health + halfHearts, MaxHealth); EmitSignal(SignalName.HealthChanged, _health, MaxHealth); }
	public void AddRupees(int amount) { _rupees = Mathf.Min(_rupees + amount, 999); EmitSignal(SignalName.RupeesChanged, _rupees); }

	private void SetHealth(int value)
	{
		_health = Mathf.Clamp(value, 0, MaxHealth);
		EmitSignal(SignalName.HealthChanged, _health, MaxHealth);
	}
	
	public override void _PhysicsProcess(double delta)
	{
		UpdateAir();
		
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
			int before = _charge;
			_charge = Mathf.Min(_charge + 1, SpinAtkMaxCharge);
			if (before < SpinAtkMaxCharge && _charge == SpinAtkMaxCharge)
				_swordAudio.PlayCharged();
		}
		else
		{
			if (_charge >= SpinAtkMaxCharge) 
			{
				_swordAudio.PlaySpinAttack();
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
	
	private bool ShowJumpPose() {
		string animation = "jump-" + FacingDirection();
		if (!_sprite.SpriteFrames.HasAnimation(animation))
			return false;

		// The original steps to the next jump frame every 8 ticks and holds the last one.
		int frame = Mathf.Min(_airTicks / 8, 2);
		frame = Mathf.Min(frame, _sprite.SpriteFrames.GetFrameCount(animation) - 1);

		_sprite.Animation = animation;
		_sprite.Frame = frame;
		_sprite.Pause();
		_sprite.FlipH = _facing.X < 0;
		return true;
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
		
		if (InAir && ShowJumpPose())
			return;

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
		_swordAudio.PlaySwing();
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
		ProbeSword();

		_swingFrame++;
		if (_swingFrame >= SwordSwingFrames) {
			_swingFrame = -1;
			if (!_swordOut)
				_sword.HideSword();
		}
	}
	
	private int SwordState() {
		if (_swingFrame >= 0) {
			if (_swingFrame < SwordSwingSteps[0]) return SwordHitbox.StateDraw;
			if (_swingFrame < SwordSwingSteps[0] + SwordSwingSteps[1]) return SwordHitbox.StateSwingStart;
			return SwordHitbox.StateSwingMiddle;
		}
		return _swordOut ? SwordHitbox.StateHolding : SwordHitbox.StateNone;
	}

	public bool TryGetSwordHitbox(out Rect2 hitbox) =>
		SwordHitbox.TryGetBox(_facing, SwordState(), GlobalPosition + SpriteTopLeftOffset, out hitbox);

	private void ProbeSword() {
		Vector2 probe = SwordHitbox.TileProbePoint(_facing, GlobalPosition + SpriteTopLeftOffset);
		InteractiveTiles.Instance?.TryInteract(probe, Interaction.Slash);
	}
}
