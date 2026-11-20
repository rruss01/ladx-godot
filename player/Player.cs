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
	private int _spinCounter = 0; // 0 = not spinning. Counts down from 32, like the original's wIsUsingSpinAttack
	public bool IsSpinning => _spinCounter > 0;

	// --- Power bracelet ---
	private int _pullTicks;                    // how long you have been pulling on a liftable object
	private CarriedObject _carried;            // what Link is lifting or carrying; null = empty-handed
	private ItemPowerBracelet _liftedWith;     // the bracelet that lifted it (power, sounds)
	public bool IsCarrying => _carried != null && IsInstanceValid(_carried);
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

		// 1b. Power bracelet: grab, pull, lift, carry, throw. True = Link is busy this tick.
		if (UpdateBracelet())
			return;

		// 2. Use item if item button pressed. (Nothing can be used while carrying: the bracelet's
		//    button throws instead, see UpdateBracelet.)
		if (!IsCarrying)
		{
			foreach (ItemButton button in Buttons) {
				if (!Input.IsActionJustPressed(ActionFor(button)))
					continue;

				_useButton = button;
				Inventory.Instance.GetEquipped(button)?.Use(this);

				if (_swingFrame >= 0)
					return;
			}
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
			// Button let go (or the sword was never out). A full charge turns into a spin.
			bool charged = _charge >= SpinAtkMaxCharge;
			_charge = 0;
			_swordOut = false;

			if (charged)
			{
				StartSpin();
				return; // the spin draws itself from the next tick on
			}

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
			// Fully charged: the sword flashes between its normal and glowing frames.
			// The original flips its palette whenever bit 2 of the frame counter changes, i.e. every 4 ticks.
			bool glow = _charge >= SpinAtkMaxCharge && (Engine.GetPhysicsFrames() & 4) != 0;
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

	// ---- Spin attack ----
	// Ported from UpdateSpinAttackAnimation (bank2.asm). The whole thing is driven by ONE counter:
	//
	//   * it starts at 32 when you let go of a fully charged sword
	//   * every tick it drops by 1, and on every 4th tick it drops by an extra 1
	//   * "counter / 4" (0-7) says which of the 8 poses to show; it counts 7, 6, ... 0, so the
	//     spin looks the same speed throughout, about 3 ticks per pose
	//   * when it reaches 0 the spin is over: 25 or 26 ticks in total
	private void StartSpin()
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
	// How it works in the original (label_1F69 in bank0.asm, EntityLiftedHandler in bank3.asm):
	//
	//   1. HOLD the bracelet's button. Link stops and holds his arms out; he can't walk or turn.
	//   2. Face the object and PULL: hold the d-pad AWAY from it for 8 ticks.
	//   3. The object slides into Link's hands and up over his head (Link is frozen for about
	//      17 ticks, or 9 with bracelet 2). The tile it sat on turns into its "lift" result.
	//   4. Link walks around carrying it. Items can't be used.
	//   5. PRESS the bracelet's button again to throw it where you are facing.
	//
	// Returns true when Link is busy and normal movement should be skipped this tick.
	private bool UpdateBracelet()
	{
		// --- Already holding something ---
		if (_carried != null)
		{
			if (!IsInstanceValid(_carried))
			{
				_carried = null; // it broke, or the screen scrolled and it was lost
			}
			else if (!_carried.IsLifted)
			{
				ShowBraceletPose("grab", 1, "idle"); // frozen in the leaning-back pose while it goes up
				return true;
			}
			else if (ThrowPressed())
			{
				_carried.Throw(_facing);
				PlayOneShot(_liftedWith?.ThrowSound);
				_carried = null;
				return true;
			}
			else
			{
				return false; // carrying: walk as normal (UpdateMovement picks the carry animation)
			}
		}

		// --- Empty-handed: is the bracelet button held? ---
		ItemPowerBracelet bracelet = FindBracelet(out ItemButton button);
		if (bracelet == null || InAir || _swordOut || !Input.IsActionPressed(ActionFor(button)))
		{
			_pullTicks = 0;
			return false;
		}

		// Holding the button: Link grabs. From here on he can't move or turn.
		Vector2 probe = LiftProbePoint();
		bool liftable = InteractiveTiles.Instance != null
			&& InteractiveTiles.Instance.CanInteract(probe, Interaction.Lift, bracelet.Power);
		bool pulling = Input.IsActionPressed(PullAction());

		// Only a pull on something liftable counts. Pulling on air or a wall starts over.
		if (!liftable || !pulling)
		{
			_pullTicks = 0;
			ShowBraceletPose("grab", 0, "idle");
			return true;
		}

		_pullTicks++;
		ShowBraceletPose("grab", 1, "idle"); // the leaning-back frame

		if (_pullTicks < bracelet.PullTicks)
			return true;

		if (InteractiveTiles.Instance.TryLift(probe, bracelet.Power, out LiftedTile tile))
		{
			_carried = CarriedObject.Create(this, tile, SwordHitbox.DirectionIndex(_facing), bracelet.LiftStageTicks);
			_liftedWith = bracelet;
			GetParent().AddChild(_carried);
			PlayOneShot(bracelet.LiftSound);
		}

		_pullTicks = 0;
		return true;
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

	// Throw on a fresh press of the bracelet's button, as in the original. If the bracelet has been
	// unequipped in the meantime, either button throws (the original does the same).
	private bool ThrowPressed()
	{
		if (FindBracelet(out ItemButton button) != null)
			return Input.IsActionJustPressed(ActionFor(button));

		return Input.IsActionJustPressed(ActionFor(ItemButton.A)) || Input.IsActionJustPressed(ActionFor(ItemButton.B));
	}

	// The d-pad direction opposite to the way Link faces: the one you hold to pull.
	private string PullAction()
	{
		if (_facing.X > 0) return "ui_left";
		if (_facing.X < 0) return "ui_right";
		return _facing.Y < 0 ? "ui_down" : "ui_up";
	}

	// The one spot in front of Link that is checked for something to lift, from the original's
	// SwordAreaXForDirection / SwordAreaYForDirection. Offsets are from the CENTER of Link's sprite.
	// Right is 5 where the original says 4, because our collision box is half a pixel wider
	// and would otherwise stop Link just short of the tile.
	private static readonly Vector2[] LiftProbe =
	{
		new Vector2(5, 2),   // right
		new Vector2(-5, 2),  // left
		new Vector2(0, -3),  // up
		new Vector2(0, 8),   // down
	};

	private Vector2 LiftProbePoint() => GlobalPosition + LiftProbe[SwordHitbox.DirectionIndex(_facing)];

	// Shows "name-direction" (e.g. grab-right) if the sprite sheet has it, otherwise the fallback
	// animation, so the code works before every pose has been drawn.
	private void ShowBraceletPose(string name, int frame, string fallback)
	{
		string animation = $"{name}-{FacingDirection()}";
		if (!_sprite.SpriteFrames.HasAnimation(animation))
			animation = $"{fallback}-{FacingDirection()}";

		_sprite.Animation = animation;
		_sprite.Frame = Mathf.Min(frame, _sprite.SpriteFrames.GetFrameCount(animation) - 1);
		_sprite.Pause();
		_sprite.FlipH = _facing.X < 0;
	}

	private void PlayOneShot(AudioStream sound)
	{
		if (sound == null)
			return;

		AudioStreamPlayer p = new AudioStreamPlayer { Stream = sound };
		p.Finished += p.QueueFree;
		AddChild(p);
		p.Play();
	}
}
