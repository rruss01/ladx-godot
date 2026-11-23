using Godot;

public partial class Player : CharacterBody2D
{
	[Signal] public delegate void HealthChangedEventHandler(int health, int maxHealth);
	[Signal] public delegate void RupeesChangedEventHandler(int amount);

	private int _health;
	private int _rupees;

	// health/rupee mgmt
	public void TakeDamage(int amount) => SetHealth(_health - amount);
	public void Heal(int amount) => SetHealth(_health + amount);
	public void AddHealth(int halfHearts) { _health = Mathf.Min(_health + halfHearts, MaxHealth); EmitSignal(SignalName.HealthChanged, _health, MaxHealth); }
	public void AddRupees(int amount) { _rupees = Mathf.Min(_rupees + amount, 999); EmitSignal(SignalName.RupeesChanged, _rupees); }

	// debug value
	[Export] public int MaxHealth { get; set; } = 6;

	// --- Movement (values from LADX disassembly) ---
	private const int StraightSpeed = 16; // $10
	private const int DiagonalSpeed = 12; // $0C
	private Vector2I _facing = Vector2I.Down;
	// Y starts at 8 so the axes don't pause on the same frame (fixes diagonal jitter).
	private Vector2I _subpixel = new Vector2I(0, 8);
	public Vector2I Facing => _facing;
	private bool _canMove = true;
	public void LockMovement() => _canMove = false;

	// --- Sword ---
	private static readonly int[] SwordSwingSteps = [3, 3, 8]; // draw, swing start, swing middle
	private const int SwordSwingFrames = 14; // sum of SwordSwingSteps
	private const int SwordReach = 12;
	private int _swingFrame = -1; // -1 = not swinging
	private int _spinCounter = 0; // 0 = not spinning. Counts down from 32, from LADX's wIsUsingSpinAttack
	private AnimatedSprite2D _sprite;
	private SwordSprite _sword;
	private SwordAudio _swordAudio;
	public bool IsSpinning => _spinCounter > 0;
	public int Charge { get; set; } = 0;
	public bool Charged { get; set; }
	public bool ShowSwordHitbox { get; set; } = false;
	public Vector2 SpriteTopLeftOffset { get; set; } = new Vector2(-8, -8);
	public bool SwordOut { get; private set; }

	// --- Power bracelet ---
	private CarriedObject _carried;            // what Link is lifting or carrying; null = empty-handed
	private ItemPowerBracelet _liftedWith;     // the bracelet that lifted it (power, sounds)
	public bool IsCarrying => _carried != null && IsInstanceValid(_carried);
	public int PullProgress { get; private set; }
	public int AdvancePull() => ++PullProgress;
	public void ResetPull() => PullProgress = 0;

	// --- Roc's Feather ---
	private const int JumpVelocity = 32; // $20
	private const int Gravity = 2;
	private int _z;
	private int _vz;
	private int _airTicks;
	private Vector2 _spriteBase, _swordBase;
	public bool InAir { get; private set; }
	public float Height => _z / 16f;
	
	// buttons; input handling
	private static readonly ItemButton[] Buttons = { ItemButton.B, ItemButton.A };
	private static string ActionFor(ItemButton b) => b == ItemButton.B ? "button_b" : "button_a";
	public ItemButton SwordButton { get; private set; }
	private readonly bool[] _wasHeld = new bool[2];

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
		if (!InAir) { return; }
		
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

	private void SetHealth(int value)
	{
		_health = Mathf.Clamp(value, 0, MaxHealth);
		EmitSignal(SignalName.HealthChanged, _health, MaxHealth);
	}

	private void UpdateButtonEdges() {
		foreach (ItemButton button in Buttons)
		{
			bool held = Input.IsActionPressed(ActionFor(button));
			if (_wasHeld[(int)button] && !held) {
				Inventory.Instance.GetEquipped(button)?.OnRelease(this, button);
				
				if (button == SwordButton && SwordOut)
					EndSwordHold();
			}
			_wasHeld[(int)button] = held;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		_canMove = true;
		UpdateButtonEdges();
		UpdateAir();
		
		// 0. Spin attack in progress: movement fully blocked, only the spin runs
		if (_spinCounter > 0)
		{
			UpdateSpin();
			return;
		}
		// 1. Swing in progress: movement fully blocked, only the swing runs
		if (_swingFrame >= 0)
		{
			UpdateSwing();
			return;
		}

		if (UpdateBracelet())
			return;

		// 2. Use item if item button pressed, as long as nothing is being carried.
		if (!IsCarrying)
		{
			foreach (ItemButton button in Buttons) {
				if (!Input.IsActionJustPressed(ActionFor(button))) { continue; }
				Inventory.Instance.GetEquipped(button)?.OnPress(this, button);
				if (_swingFrame >= 0) { return; }
			}
		}

		foreach (ItemButton button in Buttons)
			if (Input.IsActionPressed(ActionFor(button)))
				Inventory.Instance.GetEquipped(button)?.OnHeld(this, button);
		
		if (!_canMove) { return; }

		// otherwise move normally
		UpdateMovement();
	}

	public void EndSwordHold()
	{
		Charge = 0;
		Charged = false;
		SwordOut = false;
		_sword.HideSword();
	}

	public void PlayChargedSound()
	{
		_swordAudio.PlayCharged();
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
		if (dpad.Count == 1 && !SwordOut)
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

		if (SwordOut) {
			// Fully charged: the sword flashes between its normal and glowing frames.
			// The original flips its palette whenever bit 2 of the frame counter changes, i.e. every 4 ticks.
			bool glow = Charged && (Engine.GetPhysicsFrames() & 4) != 0;
			_sword.ShowPose(_facing, glow ? _sword.GlowPose : _sword.HoldPose);
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
		if (IsCarrying)
			state = $"carry-{state}"; // both hands are busy, so no shield
		else if (Inventory.Instance.GetEquipped(ItemButton.B)?.DisplayName == "shield1" || Inventory.Instance.GetEquipped(ItemButton.A)?.DisplayName == "shield1")
			state = $"shield{state}";
		_sprite.Play($"{state}-{FacingDirection()}");
		_sprite.FlipH = _facing.X < 0;
	}

	public void StartSwing(bool chargeable, ItemButton button)
	{
		SwordButton = button;
		SwordOut = chargeable;
		UpdateFacing(ReadDpad());
		Velocity = Vector2.Zero;
		Charge = 0;
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
			if (!SwordOut)
				_sword.HideSword();
		}
	}
	
	private int SwordState() {
		if (_swingFrame >= 0) {
			if (_swingFrame < SwordSwingSteps[0]) return SwordHitbox.StateDraw;
			if (_swingFrame < SwordSwingSteps[0] + SwordSwingSteps[1]) return SwordHitbox.StateSwingStart;
			return SwordHitbox.StateSwingMiddle;
		}
		return SwordOut ? SwordHitbox.StateHolding : SwordHitbox.StateNone;
	}

	public bool TryGetSwordHitbox(out Rect2 hitbox)
	{
		// While spinning, the sword hurts everything in a 48x48 box around Link.
		if (_spinCounter > 0)
		{
			hitbox = SwordHitbox.SpinBox(GlobalPosition + SpriteTopLeftOffset);
			return true;
		}

		return SwordHitbox.TryGetBox(_facing, SwordState(), GlobalPosition + SpriteTopLeftOffset, out hitbox);
	}

	private void ProbeSword() {
		Vector2 probe = SwordHitbox.TileProbePoint(_facing, GlobalPosition + SpriteTopLeftOffset);
		InteractiveTiles.Instance?.TryInteract(probe, Interaction.Slash);
	}

	public void StartSpin()
	{
		_spinCounter = SpinAttack.StartCounter;
		_swordAudio.PlaySpinAttack();
	}

	private void UpdateSpin()
	{
		_spinCounter--;

		// Pose number 7..0 becomes step 0..7 of the table, which is written in time order.
		int pose = _spinCounter >> 2;
		int step = 7 - pose;
		int facing = SwordHitbox.DirectionIndex(_facing);

		int bodyDirection = SpinAttack.BodyDirection[facing][step];
		int swordDirection = SpinAttack.SwordDirection[facing][step];

		// Link's body has its own frame for every direction, so it never needs flipping here.
		_sprite.Animation = "spin-attack";
		_sprite.Frame = SpinAttack.BodyFrame(bodyDirection);
		_sprite.Pause();
		_sprite.FlipH = false;

		_sword.ShowSpinPose(swordDirection);

		// Cut whatever is in the tile the sword tip is touching, every tick, like a normal swing.
		Vector2 probe = GlobalPosition + SpriteTopLeftOffset + SpinAttack.TileProbe[swordDirection];
		InteractiveTiles.Instance?.TryInteract(probe, Interaction.Slash);

		// The extra drop. The original uses its free-running frame counter (hFrameCounter & 3);
		// Godot's physics frame count is the same idea, a counter that never resets.
		if ((Engine.GetPhysicsFrames() & 3) == 0)
			_spinCounter--;

		if (_spinCounter <= 0)
		{
			_spinCounter = 0;
			_sword.HideSword();
		}
	}

	// ---- Power bracelet ----
	private bool UpdateBracelet()
	{
		// --- Already holding something ---
		if (_carried == null)
			return false;
		
		if (!IsInstanceValid(_carried))
		{
			_carried = null;
			return false;
		}

		if (!_carried.IsLifted)
		{
			ShowPose("grab", 1, "idle");
			return true;
		}

		if (ThrowPressed())
		{
			_carried.Throw(_facing);
			PlayOneShot(_liftedWith?.ThrowSound);
			_carried = null;
			return true;
		}

		return false;
	}

	public void BeginCarrying(CarriedObject carried, ItemPowerBracelet bracelet)
	{
		_carried = carried;
		_liftedWith = bracelet;
		GetParent().AddChild(carried);
	}

	// Which equipped item is the bracelet, and on which button? Null if neither is.
	private ItemPowerBracelet FindBracelet(out ItemButton button)
	{
		foreach (ItemButton b in Buttons)
		{
			if (Inventory.Instance.GetEquipped(b) is ItemPowerBracelet bracelet)
			{
				button = b;
				return bracelet;
			}
		}

		button = ItemButton.B;
		return null;
	}

	// Throw on a fresh press of the bracelet's button. If the bracelet has been
	// unequipped in the meantime, either button throws.
	private bool ThrowPressed()
	{
		if (FindBracelet(out ItemButton button) != null)
			return Input.IsActionJustPressed(ActionFor(button));

		return Input.IsActionJustPressed(ActionFor(ItemButton.A)) || Input.IsActionJustPressed(ActionFor(ItemButton.B));
	}

	// Shows "name-direction" (e.g. grab-right) if the sprite sheet has it, otherwise the fallback
	// animation, so the code works before every pose has been drawn.
	public void ShowPose(string name, int frame, string fallback)
	{
		string animation = $"{name}-{FacingDirection()}";
		if (!_sprite.SpriteFrames.HasAnimation(animation))
			animation = $"{fallback}-{FacingDirection()}";

		_sprite.Animation = animation;
		_sprite.Frame = Mathf.Min(frame, _sprite.SpriteFrames.GetFrameCount(animation) - 1);
		_sprite.Pause();
		_sprite.FlipH = _facing.X < 0;
	}

	public void PlayOneShot(AudioStream sound)
	{
		if (sound == null)
			return;

		AudioStreamPlayer p = new AudioStreamPlayer { Stream = sound };
		p.Finished += p.QueueFree;
		AddChild(p);
		p.Play();
	}
}
