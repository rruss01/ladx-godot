using Godot;
using System;

public partial class Game : Node2D
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		var player = GetNode<Player>("Player");
		var hud = GetNode<Hud>("HUD");
		var audioPlayer = GetNode<MusicManager>("MusicManager");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
