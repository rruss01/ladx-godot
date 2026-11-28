using Godot;

public enum ItemButton { B = 0, A = 1 }
public enum AmmoType { Bombs, Arrows, Powder }

public partial class Inventory : Node
{
	public const int ItemSlots = 10;
	public static Inventory Instance { get; private set; }
	private static readonly int[][] AmmoMax = {
	[30, 60], // Bombs
	[30, 60], // Arrows
	[20, 40]  // Magic Powder
	};
	
	[Signal] public delegate void SlotChangedEventHandler(int slot, ItemData item);
	[Signal] public delegate void EquippedChangedEventHandler(int button, ItemData item);
	[Signal] public delegate void AmmoChangedEventHandler();
	[Signal] public delegate void HealthChangedEventHandler(int health, int maxHealth);
	[Signal] public delegate void RupeesChangedEventHandler(int amount);
	[Signal] public delegate void RupeesSteppedEventHandler();
	
	// DEBUG
	[Export] public ItemData StartingB { get; set; }
	[Export] public ItemData StartingA { get; set; }
	[Export] public Godot.Collections.Array<ItemData> StartingItems { get; set; } = new();
		
	private readonly ItemData[] _slots = new ItemData[ItemSlots];
	private readonly ItemData[] _equipped = new ItemData[2];
	public Godot.Collections.Array<ItemData> CurrentItems;

	private readonly int[] _ammoCount = new int[AmmoMax.Length];
	private readonly int[] _ammoLevel = new int[AmmoMax.Length];


	public int GetAmmo(AmmoType type) => _ammoCount[(int)type];
	public int GetMaxAmmo(AmmoType type) => AmmoMax[(int)type][_ammoLevel[(int)type]];
	
	public int RupeeCount = 0;
	private const int MaxRupees = 999;
	private int _addBuffer;
	private int _subBuffer;
	
	public int CurrentHealth = 6;
	public int MaxHealth = 6;
	
	public void AddRupees(int amount) => _addBuffer += amount;
	public void SpendRupees(int amount) => _subBuffer += amount;
	public bool HasRupees(int amount) => RupeeCount - _subBuffer >= amount;

	private static int StepFor(int buffer) => Mathf.Min(buffer / 32 + 1, 9);

	public override void _PhysicsProcess(double delta)
	{
		if ((Engine.GetPhysicsFrames() & 1) != 0) return;

		bool stepped = false;

		if (_addBuffer > 0)
		{
			int step = StepFor(_addBuffer);
			_addBuffer -= step;
			RupeeCount += step;
			if (RupeeCount > MaxRupees)
			{
				RupeeCount = MaxRupees;
				_addBuffer = 0;
			}
			stepped = true;
		}

		if (_subBuffer > 0)
		{
			int step = StepFor(_subBuffer);
			_subBuffer -= step;
			RupeeCount -= step;
			if (RupeeCount < 0)
			{
				RupeeCount = 0;
				_subBuffer = 0;
			}
			stepped = true;
		}
		if (stepped)
		{
			EmitSignal(SignalName.RupeesChanged, RupeeCount);
			EmitSignal(SignalName.RupeesStepped);
		}
	}

	public void AddAmmo(AmmoType type, int amount) {
		_ammoCount[(int)type] = Mathf.Min(GetAmmo(type) + amount, GetMaxAmmo(type));
		EmitSignal(SignalName.AmmoChanged);
	}
	public void AddHealth(int halfHearts) { 
		CurrentHealth = Mathf.Min(CurrentHealth + halfHearts, MaxHealth); 
		EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);
	}
	
	public void AddHeartContainer() {
		MaxHealth += 2; // units are in half hearts
		while (CurrentHealth < MaxHealth) {
			CurrentHealth++;
			EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);
		}
	}

	private void SetHealth(int value)
	{
		CurrentHealth = Mathf.Clamp(value, 0, MaxHealth);
		EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);
	}

	// rewrite to TrySpendAmmo?
	public bool TrySpendAmmo(AmmoType type, int amount) {
		if (GetAmmo(type) < amount) return false;
		_ammoCount[(int)type] -= amount;
		EmitSignal(SignalName.AmmoChanged);
		return true;
	}

	public void UpgradeAmmo(AmmoType type)
	{
		int level = _ammoLevel[(int)type];
		if (level + 1 >= AmmoMax[(int)type].Length) return;
		_ammoLevel[(int)type] = level + 1;
		_ammoCount[(int)type] = GetMaxAmmo(type);
		EmitSignal(SignalName.AmmoChanged);
	}
	
	public override void _EnterTree() {
		Instance = this;
	}
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		SetHealth(MaxHealth);
		AddAmmo(AmmoType.Bombs, 20);
		AddAmmo(AmmoType.Arrows, 20);
		AddAmmo(AmmoType.Powder, 20);
		CurrentItems = StartingItems;
		_equipped[(int)ItemButton.B] = StartingB;
		_equipped[(int)ItemButton.A] = StartingA;
		for (int i = 0; i < CurrentItems.Count && i < ItemSlots; i++)
			_slots[i] = CurrentItems[i];
	}
	
	public ItemData GetSlot(int slot) => _slots[slot];
	public ItemData GetEquipped(ItemButton button) => _equipped[(int)button];
	
	// Attempt to add item to inventory
	public bool Add(ItemData item)
	{
		if (item == null || Has(item))
			return false;

		// Item upgrade?
		if (item.Replaces != null) {
			if (_equipped[0] == item.Replaces) {
				_equipped[0] = item;
				EmitSignal(SignalName.EquippedChanged, 0, item);
				return true;
			}
			if (_equipped[1] == item.Replaces) {
				_equipped[1] = item;
				EmitSignal(SignalName.EquippedChanged, 1, item);
				return true;
			}
			for (int i = 0; i < ItemSlots; i++) {
				if (_slots[i] == item.Replaces)
				{
					_slots[i] = item;
					EmitSignal(SignalName.SlotChanged, i, item);
					return true;
				}
			}
		}
		// Get next empty slot
		for (int i = 0; i < ItemSlots; i++)
		{
			if (_slots[i] == null)
			{
				_slots[i] = item;
				EmitSignal(SignalName.SlotChanged, i, item);
				return true;
			}
		}
		return false; // No room. Should never run.
	}

	public bool Has(ItemData item)
	{
		if (_equipped[0] == item || _equipped[1] == item)
			return true;
		foreach (ItemData slotItem in _slots)
			if (slotItem == item)
				return true;
		return false;
	}

	public void Equip(int slot, ItemButton button)
	{
		ItemData chosen = _slots[slot];

		ItemData previous = _equipped[(int)button];
		_slots[slot] = previous;
		_equipped[(int)button] = chosen;

		EmitSignal(SignalName.SlotChanged, slot, previous);
		EmitSignal(SignalName.EquippedChanged, (int)button, chosen);
	}
	
	public void Remove(ItemData item) {
	for (int b = 0; b < _equipped.Length; b++)
		if (_equipped[b] == item)
		{
			_equipped[b] = null;
			EmitSignal(SignalName.EquippedChanged, b, (ItemData)null);
		}

	for (int i = 0; i < ItemSlots; i++)
		if (_slots[i] == item)
		{
			_slots[i] = null;
			EmitSignal(SignalName.SlotChanged, i, (ItemData)null);
		}
	}
}
