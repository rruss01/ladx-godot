using Godot;

public enum ItemButton { B = 0, A = 1 }

public partial class Inventory : Node
{
	public const int ItemSlots = 10;
	public static Inventory Instance { get; private set; }
	
	[Signal] public delegate void SlotChangedEventHandler(int slot, ItemData item);
	[Signal] public delegate void EquippedChangedEventHandler(int button, ItemData item);
	[Signal] public delegate void AmmoChangedEventHandler();
	[Signal] public delegate void RupeesChangedEventHandler(int amount);
	[Signal] public delegate void HealthChangedEventHandler(int health, int maxHealth);

	// DEBUG
	[Export] public ItemData StartingB { get; set; }
	[Export] public ItemData StartingA { get; set; }
	[Export] public Godot.Collections.Array<ItemData> StartingItems { get; set; } = new();
		
	private readonly ItemData[] _slots = new ItemData[ItemSlots];
	private readonly ItemData[] _equipped = new ItemData[2];
	public Godot.Collections.Array<ItemData> CurrentItems;
	
	public int BombCount = 20;
	public int BombBagLevel = 0;
	public readonly int[] MaxBombs = [20, 60];
	
	public int ArrowCount = 20;
	public int QuiverLevel = 0;
	public readonly int[] MaxArrows = [20, 60];
	
	public int PowderCount = 20;
	public int PowderLevel = 0;
	public readonly int[] MaxPowder = [20, 40];
	
	public int RupeeCount = 0;
	private readonly int _maxRupees = 999;
	
	public int CurrentHealth = 6;
	public int MaxHealth = 6;
	
	public void AddBomb(int amount = 1) {
		BombCount = Mathf.Min(BombCount + amount, MaxBombs[BombBagLevel]);
		EmitSignal(SignalName.AmmoChanged);
	}
	
	public void AddArrow(int amount = 5) {
		ArrowCount = Mathf.Min(ArrowCount + amount, MaxArrows[QuiverLevel]);
		EmitSignal(SignalName.AmmoChanged);
	}
	
	public void AddPowder(int amount = 5) {
		PowderCount = Mathf.Min(PowderCount + amount, MaxPowder[PowderLevel]);
		EmitSignal(SignalName.AmmoChanged);
	}
	
	public void AddRupees(int amount) { 
		RupeeCount = Mathf.Min(RupeeCount + amount, _maxRupees); 
		EmitSignal(SignalName.RupeesChanged, RupeeCount); 
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
	public bool TryPlaceBomb() {
		if (BombCount <= 0) return false;
		BombCount--;
		EmitSignal(SignalName.AmmoChanged);
		return true;
	}
	
	public override void _EnterTree() {
		Instance = this;
	}
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		SetHealth(6);
		AddRupees(0);
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
}
