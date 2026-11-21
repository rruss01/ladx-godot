// ---- Power bracelet ----
// How it works in the original (label_1F69 in bank0.asm, EntityLiftedHandler in bank3.asm):
//
//   1. HOLD the bracelet's button. Player stops and holds his arms out; he can't walk or turn.
//   2. Face the object and PULL: hold the d-pad AWAY from it for 8 ticks.
//   3. The object slides into the player's hands and up over his head (the player is frozen for about
//      17 ticks, or 9 with bracelet 2). The tile it sat on turns into its "lift" result.
//   4. The player walks around carrying it. Items can't be used.
//   5. Press the bracelet's button again to throw it where you are facing.
//
// Returns true when the player is busy and normal movement should be skipped this tick.
// bracelet1: Power 0, LiftStageTicks 8
// bracelet2: Power 1, LiftStageTicks 4

using Godot;

[GlobalClass]
public partial class ItemPowerBracelet : ItemData
{
	// The strength of the bracelet. A tile's "p" in interactive_tiles.json is the minimum
	// Power it needs: 0 for bushes and plain rocks, 1 for the heavy rocks (bracelet 2 only).
	[Export] public int Power { get; set; } = 0;

	// How many ticks you must pull away from the object before it lifts. Original: 8
	// (3 with the Piece of Power).
	[Export] public int PullTicks { get; set; } = 8;

	// How long each of the two slow stages of the lift takes. Original: 8 for bracelet 1, 4 for bracelet 2.
	[Export] public int LiftStageTicks { get; set; } = 8;

	// Original: WAVE_SFX_LIFT_UP when the lift starts, JINGLE_FALL_DOWN when you throw.
	[Export] public AudioStream LiftSound { get; set; }
	[Export] public AudioStream ThrowSound { get; set; }

	// The one spot in front of the player that is checked for something to lift, from the original's
	// SwordAreaXForDirection / SwordAreaYForDirection. Offsets are from the CENTER of the player's sprite.
	// Right is 5 where the original says 4, because our collision box is half a pixel wider
	// and would otherwise stop him just short of the tile.
	private static readonly Vector2[] LiftProbe =
	[
		new Vector2(5, 2),   // right
		new Vector2(-5, 2),  // left
		new Vector2(0, -3),  // up
		new Vector2(0, 8),   // down
	];
	
	public Vector2 ProbePoint(Vector2 playerPos, Vector2I facing) => 
		playerPos + LiftProbe[SwordHitbox.DirectionIndex(facing)];

	// The d-pad direction opposite to the way the player faces: the one you hold to pull.
	public static string PullAction(Vector2I facing, Player player)
	{
		if (player.Facing.X > 0) return "ui_left";
		if (player.Facing.X < 0) return "ui_right";
		return player.Facing.Y < 0 ? "ui_down" : "ui_up";
	}
	
	public override void OnHeld(Player player, ItemButton button)
	{
		if (player.InAir || player.SwordOut || player.IsCarrying)
		{
			player.ResetPull();
			return;
		}

		player.LockMovement();

		Vector2 probe = ProbePoint(player.GlobalPosition, player.Facing);
		bool liftable = InteractiveTiles.Instance != null
			&& InteractiveTiles.Instance.CanInteract(probe, Interaction.Lift, Power);
		bool pulling = Input.IsActionPressed(PullAction(player.Facing, player));

		// Only a pull on something liftable counts. Pulling on air or a wall starts over.
		if (!liftable || !pulling)
		{
			player.ResetPull();
			player.ShowPose("grab", 0, "idle");
			return;
		}

		player.ShowPose("grab", 1, "idle"); // the leaning-back frame

		if (player.AdvancePull() < PullTicks)
			return;

		if (InteractiveTiles.Instance.TryLift(probe, Power, out LiftedTile tile))
		{
			CarriedObject carried = CarriedObject.Create(player, tile, SwordHitbox.DirectionIndex(player.Facing), LiftStageTicks);
			player.BeginCarrying(carried, this);
			player.PlayOneShot(LiftSound);
		}

		player.ResetPull();
	}

	public override void OnRelease(Player player, ItemButton button)
	{
		player.ResetPull();
	}
}
