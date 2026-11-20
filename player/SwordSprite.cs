using Godot;

public partial class SwordSprite : AnimatedSprite2D
{
	private readonly record struct SwingAdjustment(Vector2I Offset, bool FlipH, bool FlipV);

	private static readonly SwingAdjustment[] Up = {
		new(new Vector2I(-22, 0), false, false), // 0
		new(new Vector2I(-22, -22), false, false), // 1
		new(new Vector2I(0, -22), false, true), // 2
		new(new Vector2I(0, -22), false, true) // 3
	};

	private static readonly SwingAdjustment[] Down = {
		new(new Vector2I(6, 1), true, false), // 0
		new(new Vector2I(6, 8), true, true), // 1
		new(new Vector2I(-7, 8), false, false), // 2
		new(new Vector2I(-7, 8), false, false) // 3
	};

	private static readonly SwingAdjustment[] Left = {
		new(new Vector2I(-8, -22), true, true), // 0
		new(new Vector2I(-22, -22), false, false), // 1
		new(new Vector2I(-22, 0), false, false), // 2
		new(new Vector2I(-22, 0), false, false) // 3
	};

	private static readonly SwingAdjustment[] Right = {
		new(new Vector2I(0, -22), false, true), // 0
		new(new Vector2I(6, -22), true, false), // 1
		new(new Vector2I(6, 0), true, false), // 2
		new(new Vector2I(6, 0), true, false) // 3
	};
	
	private static readonly SwingAdjustment[] SpinAtk = {
		new(new Vector2I(6, 8), true, true), // 0
		new(new Vector2I(6, 0), true, false), // 1
		new(new Vector2I(6, -22), true, false), // 2
		new(new Vector2I(0, -22), false, true), // 3
		new(new Vector2I(-22, -22), false, false), // 4
		new(new Vector2I(-22, 0), false, false), // 5
		new(new Vector2I(-22, 22), false, true), // 6
		new(new Vector2I(-7, 8), false, false) // 7
	};
	
	private static readonly SwingAdjustment[] SpinAtkLeft = {
		new(new Vector2I(6, 8), true, true), // 0
		new(new Vector2I(-22, 0), false, false), // 1
		new(new Vector2I(-22, 22), false, true), // 2
		new(new Vector2I(0, -22), false, true), // 3
		new(new Vector2I(-22, -22), false, false), // 4
		new(new Vector2I(6, 0), true, false), // 5
		new(new Vector2I(6, -22), true, false), // 6
		new(new Vector2I(-7, 8), false, false) // 7
	};

	public int HoldPose = 2;
		
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		Visible = false;
	}

	public void ShowPose(Vector2I facing, int pose) {
		string animation;
		SwingAdjustment[] poses;

		if (facing.X > 0) { animation = "sword-right"; poses = Right; }
		else if (facing.X < 0) { animation = "sword-right"; poses = Left; }
		else if (facing.Y < 0) { animation = "sword-up"; poses = Up; }
		else { animation = "sword-down"; poses = Down; }

		pose = Mathf.Clamp(pose, 0, poses.Length - 1);
		SwingAdjustment adjustment = poses[pose];

		Position = adjustment.Offset;
		Animation = animation;
		Frame = pose;
		Pause();
		FlipH = adjustment.FlipH;
		FlipV = adjustment.FlipV;
		Visible = true;
	}

	public void HideSword() {
		Visible = false;
	}
	
	// DEBUG
	private static bool FlagAt(bool[] flags, int index) {
		return index < flags.Length && flags[index];
	}
}
