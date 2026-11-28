using Godot;

public partial class WandSprite : AnimatedSprite2D
{
	private readonly record struct WandPose(Vector2I Offset, bool FlipH, bool FlipV);

	private static readonly WandPose[] Down = {
		new(new Vector2I(12, 4),    false, false), // frame 0
		new(new Vector2I(-4, 12),   false, true)  // frame 1
	};
	private static readonly WandPose[] Up = {
		new(new Vector2I(-12, 4),  true, false),
		new(new Vector2I(4, -12),  false, false)
	};
	private static readonly WandPose[] Right = {
		new(new Vector2I(7, -12),  false, false),
		new(new Vector2I(15, 4),    false, false)
	};
	private static readonly WandPose[] Left = {
		new(new Vector2I(-7, -12), true, false),
		new(new Vector2I(-15, 4),  true, false)
	};

	public override void _Ready() => Visible = false;

	public void ShowPose(Vector2I facing, int frame)
	{
		string animation;
		WandPose[] poses;

		if (facing.X > 0) { animation = "wand-right"; poses = Right; }
		else if (facing.X < 0) { animation = "wand-right"; poses = Left; }
		else if (facing.Y < 0) { animation = "wand-up"; poses = Up; }
		else { animation = "wand-down"; poses = Down; }

		WandPose pose = poses[Mathf.Clamp(frame, 0, poses.Length - 1)];
		Position = pose.Offset;
		Animation = animation;
		Frame = frame;
		Pause();
		FlipH = pose.FlipH;
		FlipV = pose.FlipV;
		Visible = true;
	}

	public void HideWand() => Visible = false;
}
