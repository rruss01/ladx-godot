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
	
	public int BombCount = 20;
	public int BombBagLevel = 0;
	public int[] MaxBombs = [20, 30, 40];
	public int RupeeCount = 0;
	public int CurrentHealth = 6;
	public int MaxHealth = 6;
	private readonly int _maxRupees = 999;
	
	public void AddBomb(int amount = 1) {
		BombCount = Mathf.Min(BombCount + amount, MaxBombs[BombBagLevel]);
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

	private void SetHealth(int value)
	{
		CurrentHealth = Mathf.Clamp(value, 0, MaxHealth);
		EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);
	}

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
		_equipped[(int)ItemButton.B] = StartingB;
		_equipped[(int)ItemButton.A] = StartingA;
		for (int i = 0; i < StartingItems.Count && i < ItemSlots; i++)
			_slots[i] = StartingItems[i];
	}
	
	public ItemData GetSlot(int slot) => _slots[slot];
	public ItemData GetEquipped(ItemButton button) => _equipped[(int)button];
	
	// Pickups call this. Returns false if the item is already owned or there's no room.
	public bool Add(ItemData item)
	{
		if (item == null || Has(item))
			return false;

		for (int i = 0; i < ItemSlots; i++)
		{
			if (_slots[i] == null)
			{
				_slots[i] = item;
				EmitSignal(SignalName.SlotChanged, i, item);
				return true;
			}
		}
		return false;
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
