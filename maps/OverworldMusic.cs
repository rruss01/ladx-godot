using Godot;

// Which song plays in each of the overworld's 16 x 16 rooms.
// This is the original's OverworldMusicTracks table (src/code/audio/select_music_track.asm),
// redrawn as a picture: one letter per room, 16 letters per row, row 0 at the top.
// Edit the letters to move a region's border.
//
//   T = Tal Tal Range     O = Overworld        F = Mysterious Forest
//   R = Raft Ride Rapids  M = Mabe Village     A = Animal Village
public static class OverworldMusic
{
	private static readonly string[] Rows =
	{
		"TTTTTTTTTTTTTTTT", // 0
		"TTTTTTTTTTTTTTTT", // 1
		"OOOOOOOOOOOOOOOO", // 2
		"OOOOOOOOOOOOOOOO", // 3
		"FFFFOOOOOOOORRRR", // 4
		"FFFFOOOOOOOORRRR", // 5
		"FFFFOOOOOOOORRRR", // 6
		"FFFFOOOOOOOORRRR", // 7
		"FMMMOOOOOOOOOOOO", // 8
		"MMMMOOOOOOOOOOOO", // 9
		"MMMMOOOOOOOOOOOO", // 10
		"MMMMOOOOOOOOOOOO", // 11
		"OOOOOOOOOOOOAAOO", // 12
		"OOOOOOOOOOOOAAOO", // 13
		"OOOOOOOOOOOOOOOO", // 14
		"OOOOOOOOOOOOOOOO", // 15
	};

	// room = (column, row), the same Vector2I ScreenScroller.RoomChanged gives you.
	public static MusicTrack ForRoom(Vector2I room)
	{
		if (room.Y < 0 || room.Y >= Rows.Length || room.X < 0 || room.X >= Rows[room.Y].Length)
			return MusicTrack.Overworld;

		return Rows[room.Y][room.X] switch
		{
			'T' => MusicTrack.TalTalRange,
			'F' => MusicTrack.MysteriousForest,
			'R' => MusicTrack.RaftRideRapids,
			'M' => MusicTrack.MabeVillage,
			'A' => MusicTrack.AnimalVillage,
			_ => MusicTrack.Overworld,
		};
	}
}
