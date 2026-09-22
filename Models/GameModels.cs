using System.Numerics;

namespace EpicCottonGame.Models;

public class CottonType
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Chance { get; set; } = "1/1";
    public int[] Dmg { get; set; } = new[] { 1, 2 };
    public int Value { get; set; }
    public string GrowImg { get; set; } = "";
    public string ReadyImg { get; set; } = "";
    public double ChanceFraction { get; set; } = 1;
}

public class MutationConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Chance { get; set; } = "1/100";
    public double ValueMultiplier { get; set; } = 1;
    public double DamageMultiplier { get; set; } = 1;
    public string Icon { get; set; } = "";
    public double ChanceFraction { get; set; }
}

public class CottonInstance
{
    public string Id { get; }
    public CottonInstance() : this(Guid.NewGuid().ToString("N")) { }
    public CottonInstance(string id) => Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
    public CottonType Type { get; set; } = null!;
    public MutationConfig? Mutation { get; set; }
    public string DisplayName => Mutation == null ? Type.Name : $"{Mutation.Name} {Type.Name}";
}

public class ShopItemConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public double CostMultiplier { get; set; } = 2;
    public BigInteger BaseCost { get; set; }
    /// <summary>Damage reduction percentage gained per Glove tier (after Tier 1).</summary>
    public double EffectPerTier { get; set; }
    public int MaxHpPerTier { get; set; }
    public int CapacityPerTier { get; set; }
    public int ValuePerTier { get; set; }
    public string Icon { get; set; } = "";
}

public class ShopConfig
{
    public List<ShopItemConfig> Items { get; set; } = new();
}

public class MarketItemConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public string Kind { get; set; } = "";
    public BigInteger Cost { get; set; }
    public string Icon { get; set; } = "";
    public int MaxStock { get; set; }
    public double RarityMultiplier { get; set; } = 1;
    public double MutationMultiplier { get; set; } = 1;
    public double MutationChanceBonus { get; set; }
}

public class GlobalMarketConfig
{
    public int RestockIntervalSeconds { get; set; } = 45;
    public List<MarketItemConfig> Items { get; set; } = new();
}

public enum StemState
{
    Growing,
    Ready
}

public sealed class CottonStem
{
    public int X { get; set; }
    public int Y { get; set; }
    public StemState State { get; set; } = StemState.Growing;
    public CottonType? Type { get; set; }
    public MutationConfig? Mutation { get; set; }
    public DateTime ReadyAt { get; set; }
    public HashSet<string> FertilizerIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public double RarityMultiplier { get; set; } = 1;
    public double MutationMultiplier { get; set; } = 1;
    public double MutationChanceBonus { get; set; }

    public bool IsFertilized => FertilizerIds.Count > 0;
    public bool HasFertilizer(string fertilizerId) => FertilizerIds.Contains(fertilizerId);
}
