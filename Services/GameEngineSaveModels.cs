using EpicCottonGame.Models;

namespace EpicCottonGame.Services;

public sealed class GameSaveState
{
    public int Version { get; set; } = 9;
    public string Money { get; set; } = "0";
    public int Hp { get; set; }
    public int GloveTier { get; set; } = 1;
    public int BasketTier { get; set; } = 1;
    public bool IsDead { get; set; }
    public string LastLostMoney { get; set; } = "0";
    public long NextMarketRestockUtcTicks { get; set; }
    public Dictionary<string, int> Inventory { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> MarketStock { get; set; } = new(StringComparer.Ordinal);
    public List<string> FavoriteCottonIds { get; set; } = new();
    public List<GameSaveStem> Stems { get; set; } = new();
    public List<GameSaveCotton> Basket { get; set; } = new();
    public bool FirstHarvestProtectionAvailable { get; set; } = true;
}

public sealed class GameSaveStem
{
    public int X { get; set; }
    public int Y { get; set; }
    public StemState State { get; set; }
    public string? TypeId { get; set; }
    public string? MutationId { get; set; }
    public long ReadyAtUtcTicks { get; set; }
    // Legacy field retained so version 8 saves can still be migrated.
    public string? FertilizerId { get; set; }
    public List<string> FertilizerIds { get; set; } = new();
    public double RarityMultiplier { get; set; } = 1;
    public double MutationMultiplier { get; set; } = 1;
    public double MutationChanceBonus { get; set; }
}

public sealed class GameSaveCotton
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TypeId { get; set; } = "common";
    public string? MutationId { get; set; }
}

public sealed class GameSaveCottonListing
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public GameSaveCotton Item { get; set; } = new();
    public string Price { get; set; } = "0";
}
