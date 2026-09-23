using System.Globalization;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using EpicCottonGame.Models;

namespace EpicCottonGame.Services;

/// <summary>Authoritative client-side game state for the single-player GitHub Pages build.</summary>
public sealed class GameEngine
{
    public const int BaseMaxHp = 100;
    public const int Hearts = 5;
    public const int SeedInventoryCap = 9999;
    public const int RegrowMinSeconds = 10;
    public const int RegrowMaxSeconds = 60;
    public const int SeedGrowthMinSeconds = 10;
    public const int SeedGrowthMaxSeconds = 100;
    public const int PassiveRegenHpPerTick = 100;
    public const int PassiveRegenIntervalSeconds = 10;

    static readonly JsonSerializerOptions JsonOpts = CreateJsonOptions();
    static readonly string[] CottonConfigFiles = ["common", "fine", "premium", "rare", "royal", "epic", "mythic", "celestial", "divine", "golden"];
    readonly Random rng = new();
    readonly Dictionary<string, int> inventory = new(StringComparer.Ordinal);
    readonly Dictionary<string, MarketItemConfig> marketItemsById = new(StringComparer.Ordinal);
    readonly List<MutationConfig> mutationsByValue = new();
    readonly Dictionary<(int X, int Y), CottonStem> stems = new();

    public List<CottonType> CottonTypes { get; private set; } = new();
    public List<MutationConfig> Mutations { get; private set; } = new();
    public ShopConfig Shop { get; private set; } = new();
    public GlobalMarketConfig Market { get; private set; } = new();
    public List<CottonInstance> Basket { get; } = new();
    public HashSet<string> Favorited { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> MarketStock { get; } = new(StringComparer.Ordinal);

    public int Hp { get; private set; }
    public BigInteger Money { get; private set; }
    public int GloveTier { get; private set; } = 1;
    public int BasketTier { get; private set; } = 1;
    public bool IsDead { get; private set; }
    public DateTime NextMarketRestockAt { get; private set; }
    public BigInteger LastLostMoney { get; private set; }
    public IReadOnlyDictionary<string, int> Inventory => inventory;
    public IReadOnlyCollection<CottonStem> Stems => stems.Values;
    public int StemCount => stems.Count;
    public int ReadyStemCount => stems.Values.Count(s => s.State == StemState.Ready);
    public int GrowingStemCount => stems.Values.Count(s => s.State == StemState.Growing);
    public int MaxHp => BaseMaxHp + (GloveTier - 1) * ShopStat("glove", x => x.MaxHpPerTier, 10);
    public double HpPercent => MaxHp == 0 ? 0 : Hp / (double)MaxHp;
    public BigInteger BagCapacity => new BigInteger(8) + new BigInteger(Math.Max(0, BasketTier - 1)) * ShopStat("basket", x => x.CapacityPerTier, 4);
    public bool BagFull => new BigInteger(Basket.Count) >= BagCapacity;
    public BigInteger BestCottonValue => Basket.Count == 0 ? BigInteger.Zero : Basket.Max(SellValue);
    public bool HasBestCotton => Basket.Count > 0;
    public BigInteger NextGloveCost => ShopCost("glove", new BigInteger(100), GloveTier);
    public BigInteger NextBasketCost => ShopCost("basket", new BigInteger(150), BasketTier);
    public BigInteger MedicFullHealPrice => new BigInteger(Math.Max(1, MaxHp)) * 5 / 4;
    public bool CanUseMedic => !IsDead && Hp < MaxHp;
    public int VisibleWorldSizeHint => 20;
    public string GloveImagePath => TierAsset("glove", GloveTier);
    public string GloveCursorPath => TierAsset("glove_cursor", GloveTier);
    public string GloveCursorHoldPath => TierAsset("glove_cursor", GloveTier, true);
    public string BasketImagePath => TierAsset("basket", BasketTier);
    public int CottonSeedCount => inventory.GetValueOrDefault("cotton_seed");
    public bool FirstHarvestProtectionAvailable { get; private set; } = true;

    static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new BigIntegerJsonConverter());
        return options;
    }

    public async Task LoadConfigAsync(HttpClient http)
    {
        CottonTypes.Clear();
        Mutations.Clear();
        Shop = new();
        Market = new();
        marketItemsById.Clear();
        MarketStock.Clear();
        inventory.Clear();
        Basket.Clear();
        Favorited.Clear();
        stems.Clear();

        var cottonTasks = CottonConfigFiles.Select(file => http.GetFromJsonAsync<CottonType>($"assets/cotton/{file}.json", JsonOpts));
        var cotton = await Task.WhenAll(cottonTasks);
        CottonTypes = cotton.Where(x => x != null).Select(x => x!).ToList();
        foreach (var type in CottonTypes)
            type.ChanceFraction = ParseFraction(type.Chance);
        NormalizeChances(CottonTypes);

        var shopTask = http.GetFromJsonAsync<ShopConfig>("assets/shop.json", JsonOpts);
        var marketTask = http.GetFromJsonAsync<GlobalMarketConfig>("assets/globalmarket.json", JsonOpts);
        var mutationTask = http.GetFromJsonAsync<List<MutationConfig>>("assets/mutations.json", JsonOpts);
        await Task.WhenAll(shopTask, marketTask, mutationTask);

        Shop = shopTask.Result ?? new();
        Market = marketTask.Result ?? new();
        // The Global Market only sells one seed type plus fertilizer.
        Market.Items = Market.Items
            .Where(item => item.Id.Equals("cotton_seed", StringComparison.OrdinalIgnoreCase)
                        || item.Kind.Equals("fertilizer", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Mutations = mutationTask.Result ?? new();
        foreach (var mutation in Mutations)
            mutation.ChanceFraction = ParseFraction(mutation.Chance);
        mutationsByValue.Clear();
        mutationsByValue.AddRange(Mutations.OrderByDescending(m => m.ValueMultiplier));

        foreach (var item in Market.Items)
        {
            marketItemsById[item.Id] = item;
            // Cotton Seed is permanently available; fertilizer follows normal stock rules.
            MarketStock[item.Id] = item.Id.Equals("cotton_seed", StringComparison.OrdinalIgnoreCase) ? -1 : item.MaxStock;
        }

        NextMarketRestockAt = DateTime.UtcNow.AddSeconds(Market.RestockIntervalSeconds);
        Money = 100;
        Hp = MaxHp;
        GloveTier = 1;
        BasketTier = 1;
        IsDead = false;
        LastLostMoney = BigInteger.Zero;
        FirstHarvestProtectionAvailable = true;
        // No free seeds: the permanent starter stem is the only starting crop.
        inventory.Remove("cotton_seed");

        // One permanent starter stem begins the player's collection.
        PlantStem(0, 0, SeedGrowthMinSeconds, SeedGrowthMaxSeconds, consumeSeed: false);
    }

    static double ParseFraction(string input)
    {
        var parts = input.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return 0;
        return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
               && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
               && b > 0 ? Math.Max(0, a / b) : 0;
    }

    static void NormalizeChances(IEnumerable<CottonType> values)
    {
        var list = values.ToList();
        var total = list.Sum(x => x.ChanceFraction);
        if (total <= 0) return;
        foreach (var item in list) item.ChanceFraction /= total;
    }

    int ShopStat(string id, Func<ShopItemConfig, int> selector, int fallback)
        => selector(Shop.Items.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? new ShopItemConfig { Id = id, MaxHpPerTier = fallback, CapacityPerTier = fallback, ValuePerTier = fallback });

    double ShopDoubleStat(string id, Func<ShopItemConfig, double> selector, double fallback)
        => selector(Shop.Items.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? new ShopItemConfig { Id = id, EffectPerTier = fallback });

    BigInteger ShopCost(string id, BigInteger fallback, int tier)
    {
        var item = Shop.Items.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        var baseCost = item?.BaseCost ?? fallback;
        var exponent = Math.Clamp(item?.CostMultiplier ?? 2d, 1d, 6d);
        var t = Math.Max(1, tier);
        var scaled = Math.Ceiling(Math.Pow(t, exponent));
        if (double.IsNaN(scaled) || double.IsInfinity(scaled) || scaled < 1)
            scaled = double.MaxValue;
        return baseCost * BigInteger.Parse(scaled.ToString("0", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    CottonType RollType(CottonStem? stem)
    {
        var rarityMultiplier = Math.Max(1, stem?.RarityMultiplier ?? 1);
        double Weight(CottonType type)
        {
            var rank = Math.Max(0, CottonTypes.IndexOf(type));
            // Fertilizer improves higher rarities based on configured rarityMultiplier.
            return type.ChanceFraction * Math.Pow(rarityMultiplier, rank * 0.5d);
        }

        var total = CottonTypes.Sum(Weight);
        if (total <= 0 || CottonTypes.Count == 0)
            throw new InvalidOperationException("Cotton configuration has no valid rarity entries.");

        var roll = rng.NextDouble() * total;
        foreach (var type in CottonTypes)
        {
            var weight = Weight(type);
            if (roll < weight) return type;
            roll -= weight;
        }
        return CottonTypes[^1];
    }

    MutationConfig? RollMutation(CottonStem? stem)
    {
        var multiplier = Math.Max(1, stem?.MutationMultiplier ?? 1);
        var bonus = Math.Max(0, stem?.MutationChanceBonus ?? 0);
        if (Mutations.Count == 0) return null;

        var adjusted = mutationsByValue
            .Select(m => (Mutation: m, Chance: Math.Clamp(m.ChanceFraction * multiplier, 0, 0.95d)))
            .ToList();

        // Combine individual mutation chances into one overall mutation roll.
        // Fertilizer's bonus is applied once to that overall chance, not once per mutation.
        var noMutationProbability = 1d;
        foreach (var entry in adjusted)
            noMutationProbability *= 1d - entry.Chance;

        var totalMutationChance = Math.Clamp(1d - noMutationProbability + bonus, 0, 1.0d);
        if (rng.NextDouble() >= totalMutationChance) return null;

        // Once a mutation happens, select its type by adjusted rarity weight.
        // Rarity rank boosts rarer mutations exponentially with the fertilizer's mutation multiplier.
        double MutationWeight(MutationConfig m)
        {
            var rank = Math.Max(0, Mutations.IndexOf(m));
            return m.ChanceFraction * Math.Pow(multiplier, rank * 0.5d);
        }

        var totalWeight = Mutations.Sum(MutationWeight);
        if (totalWeight <= 0) return null;
        var roll = rng.NextDouble() * totalWeight;
        foreach (var m in Mutations)
        {
            var weight = MutationWeight(m);
            if (roll < weight) return m;
            roll -= weight;
        }
        return Mutations[^1];
    }

    DateTime RollReadyAt(int minSeconds, int maxSeconds)
        => DateTime.UtcNow.AddSeconds(rng.Next(minSeconds, maxSeconds + 1));

    public bool TryGetStem(int x, int y, out CottonStem? stem)
        => stems.TryGetValue((x, y), out stem);

    public bool CanPlantAt(int x, int y) => !IsDead && !stems.ContainsKey((x, y));

    public bool IsTargetValid(string itemId, int x, int y)
    {
        if (inventory.GetValueOrDefault(itemId) <= 0 || !marketItemsById.TryGetValue(itemId, out var item)) return false;
        if (item.Kind.Equals("seed", StringComparison.OrdinalIgnoreCase)) return CanPlantAt(x, y);
        if (item.Kind.Equals("fertilizer", StringComparison.OrdinalIgnoreCase) && stems.TryGetValue((x, y), out var stem))
            return stem.State == StemState.Growing && !stem.HasFertilizer(item.Id);
        return false;
    }

    bool PlantStem(int x, int y, int minSeconds, int maxSeconds, bool consumeSeed)
    {
        if (!CanPlantAt(x, y)) return false;
        if (consumeSeed && inventory.GetValueOrDefault("cotton_seed") <= 0) return false;
        if (consumeSeed) Consume("cotton_seed");

        stems[(x, y)] = new CottonStem
        {
            X = x,
            Y = y,
            State = StemState.Growing,
            ReadyAt = RollReadyAt(minSeconds, maxSeconds)
        };
        return true;
    }

    public bool TryUseItemAt(string itemId, int x, int y)
    {
        if (!IsTargetValid(itemId, x, y)) return false;
        var item = marketItemsById[itemId];

        if (item.Kind.Equals("seed", StringComparison.OrdinalIgnoreCase))
            return PlantStem(x, y, SeedGrowthMinSeconds, SeedGrowthMaxSeconds, consumeSeed: true);

        if (item.Kind.Equals("fertilizer", StringComparison.OrdinalIgnoreCase))
        {
            var stem = stems[(x, y)];
            if (stem.State != StemState.Growing || stem.HasFertilizer(item.Id)) return false;

            Consume(itemId);
            stem.FertilizerIds.Add(item.Id);
            RecalculateFertilizerEffects(stem);
            return true;
        }

        return false;
    }

    void RecalculateFertilizerEffects(CottonStem stem)
    {
        double rarity = 1;
        double mutation = 1;
        double bonus = 0;

        foreach (var fertilizerId in stem.FertilizerIds)
        {
            if (!marketItemsById.TryGetValue(fertilizerId, out var fertilizer)) continue;
            rarity *= Math.Max(1, fertilizer.RarityMultiplier);
            mutation *= Math.Max(1, fertilizer.MutationMultiplier);
            bonus += Math.Max(0, fertilizer.MutationChanceBonus);
        }

        stem.RarityMultiplier = Math.Clamp(rarity, 1, 1000);
        stem.MutationMultiplier = Math.Clamp(mutation, 1, 1000);
        stem.MutationChanceBonus = Math.Clamp(bonus, 0, 1.0);
    }

    public bool IsReadyAt(int x, int y)
        => stems.TryGetValue((x, y), out var stem) && stem.State == StemState.Ready && stem.Type != null;

    public bool Harvest(int x, int y)
    {
        if (IsDead || BagFull || !stems.TryGetValue((x, y), out var stem) || stem.State != StemState.Ready || stem.Type == null)
            return false;

        var type = stem.Type;
        var mutation = stem.Mutation;
        var rawDamage = Math.Max(0, RollDamage(type, mutation));
        var damageReductionPercent = GloveDamageReductionPercent;
        var damageAfterReduction = rawDamage * (1d - damageReductionPercent / 100d);
        var effectiveDamage = Math.Max(0, (int)Math.Ceiling(damageAfterReduction));
        var hpBeforeHarvest = Hp;
        var hpAfterDamage = Math.Max(0, hpBeforeHarvest - effectiveDamage);
        if (FirstHarvestProtectionAvailable && hpAfterDamage <= 0)
            Hp = 1;
        else
            Hp = hpAfterDamage;
        FirstHarvestProtectionAvailable = false;
        Basket.Add(new CottonInstance { Type = type, Mutation = mutation });
        StartRegrowth(stem);

        if (Hp <= 0) ApplyDeathPenalty();
        return true;
    }

    int RollDamage(CottonType type, MutationConfig? mutation)
    {
        var min = type.Dmg.Length > 0 ? type.Dmg[0] : 0;
        var max = type.Dmg.Length > 1 ? type.Dmg[1] : min;
        if (max < min) (min, max) = (max, min);
        var baseDamage = rng.Next(Math.Max(0, min), Math.Max(0, max) + 1);
        var multiplier = Math.Max(1d, mutation?.DamageMultiplier ?? 1d);
        var scaled = Math.Round(baseDamage * multiplier, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(scaled, 0, int.MaxValue);
    }

    void StartRegrowth(CottonStem stem)
    {
        stem.State = StemState.Growing;
        stem.Type = null;
        stem.Mutation = null;
        stem.ReadyAt = RollReadyAt(RegrowMinSeconds, RegrowMaxSeconds);
        stem.FertilizerIds.Clear();
        stem.RarityMultiplier = 1;
        stem.MutationMultiplier = 1;
        stem.MutationChanceBonus = 0;
    }

    public bool Tick()
    {
        var changed = false;
        var now = DateTime.UtcNow;

        if (!IsDead)
        {
            foreach (var stem in stems.Values)
            {
                if (stem.State != StemState.Growing || now < stem.ReadyAt) continue;
                stem.Type = RollType(stem);
                stem.Mutation = RollMutation(stem);
                stem.State = StemState.Ready;
                changed = true;
            }
        }

        if (now >= NextMarketRestockAt)
        {
            foreach (var item in Market.Items)
            {
                if (item.Id.Equals("cotton_seed", StringComparison.OrdinalIgnoreCase))
                {
                    MarketStock[item.Id] = -1;
                    continue;
                }
                MarketStock[item.Id] = item.MaxStock;
            }
            NextMarketRestockAt = now.AddSeconds(Math.Max(10, Market.RestockIntervalSeconds));
            changed = true;
        }

        return changed;
    }

    public bool PassiveRegen()
    {
        if (IsDead || Hp >= MaxHp) return false;
        var before = Hp;
        var amount = PassiveRegenHpPerTick;
        Hp = Math.Min(MaxHp, Hp + amount);
        return before != Hp;
    }

    void ApplyDeathPenalty()
    {
        IsDead = true;
        LastLostMoney = Money / 10;
        Money = Money > LastLostMoney ? Money - LastLostMoney : BigInteger.Zero;
        GloveTier = Math.Max(1, GloveTier - 1);
        BasketTier = Math.Max(1, BasketTier - 1);
    }

    public void Respawn()
    {
        IsDead = false;
        Hp = MaxHp;
    }

    public bool TryFullHeal()
    {
        if (!CanUseMedic || Money < MedicFullHealPrice) return false;
        Money -= MedicFullHealPrice;
        Hp = MaxHp;
        return true;
    }

    public bool BuyGlove()
    {
        if (Money < NextGloveCost) return false;
        Money -= NextGloveCost;
        GloveTier++;
        Hp = Math.Min(MaxHp, Hp + ShopStat("glove", x => x.MaxHpPerTier, 10));
        return true;
    }

    public bool BuyBasket()
    {
        if (Money < NextBasketCost) return false;
        Money -= NextBasketCost;
        BasketTier++;
        return true;
    }

    public bool BuyMarketItem(string itemId)
    {
        if (!marketItemsById.TryGetValue(itemId, out var item)) return false;
        if (item.Kind.Equals("seed", StringComparison.OrdinalIgnoreCase) && !item.Id.Equals("cotton_seed", StringComparison.OrdinalIgnoreCase)) return false;

        var isSeed = item.Id.Equals("cotton_seed", StringComparison.OrdinalIgnoreCase);
        var stock = MarketStock.GetValueOrDefault(itemId);
        var cost = MarketItemCost(itemId);
        if ((!isSeed && stock <= 0) || Money < cost) return false;
        if (isSeed && inventory.GetValueOrDefault(itemId) >= SeedInventoryCap) return false;

        Money -= cost;
        if (!isSeed)
            MarketStock[itemId] = stock - 1;
        inventory[itemId] = Math.Min(SeedInventoryCap, inventory.GetValueOrDefault(itemId) + 1);
        return true;
    }

    public BigInteger MarketItemCost(string itemId)
    {
        if (!marketItemsById.TryGetValue(itemId, out var item)) return BigInteger.Zero;
        if (item.Id.Equals("cotton_seed", StringComparison.OrdinalIgnoreCase))
        {
            // Smooth exponential pricing: BasePrice * 1.5^(owned-1).
            // "Owned" includes every planted stem plus every seed currently in inventory.
            // The permanent starter stem counts as the first owned unit, so the first bought seed
            // still costs the configured base price.
            var owned = Math.Max(1L, (long)stems.Count + inventory.GetValueOrDefault("cotton_seed"));
            var exponent = Math.Min(owned - 1, 10_000L);
            var basePrice = BigInteger.Max(BigInteger.One, item.Cost);
            var numerator = basePrice * BigInteger.Pow(3, (int)exponent);
            var denominator = BigInteger.Pow(2, (int)exponent);
            // Round up so the price never collapses due to integer division.
            return (numerator + denominator - BigInteger.One) / denominator;
        }
        return item.Cost;
    }

    public int GetInventoryCount(string itemId) => inventory.GetValueOrDefault(itemId);
    public int GetMarketStock(string itemId)
        => itemId.Equals("cotton_seed", StringComparison.OrdinalIgnoreCase) ? -1 : MarketStock.GetValueOrDefault(itemId);

    public IEnumerable<MarketSupplyView> GetMarketInventoryRows() => Market.Items.Select(item => new MarketSupplyView(item, inventory.GetValueOrDefault(item.Id), MarketStock.GetValueOrDefault(item.Id), MarketItemCost(item.Id)));

    public List<CottonInventoryView> GetCottonInventory() => Basket.Select(c => new CottonInventoryView(
        c.Id,
        c.DisplayName,
        c.Type.ReadyImg,
        c.Type.Value,
        c.Mutation?.Name,
        c.Mutation?.Icon ?? "",
        c.Type.Dmg.Length == 0 ? "0" : $"{c.Type.Dmg[0]:N0}-{(c.Type.Dmg.Length > 1 ? c.Type.Dmg[1] : c.Type.Dmg[0]):N0}",
        c.Mutation?.DamageMultiplier ?? 1,
        SellValue(c),
        Favorited.Contains(c.Id)
    )).ToList();
public BigInteger SellItem(string cottonId)
    {
        var index = Basket.FindIndex(x => x.Id == cottonId);
        if (index < 0 || Favorited.Contains(cottonId)) return BigInteger.Zero;
        var value = SellValue(Basket[index]);
        Basket.RemoveAt(index);
        Favorited.Remove(cottonId);
        Money += value;
        return value;
    }

    public BigInteger SellAllExceptFavorites()
    {
        var total = BigInteger.Zero;
        for (var i = Basket.Count - 1; i >= 0; i--)
        {
            if (Favorited.Contains(Basket[i].Id)) continue;
            total += SellValue(Basket[i]);
            Basket.RemoveAt(i);
        }
        Money += total;
        return total;
    }

    public BigInteger SellValue(CottonInstance cotton)
    {
        var baseValue = (BigInteger)Math.Max(0, cotton.Type.Value) * (BigInteger)Math.Max(1, Math.Round(cotton.Mutation?.ValueMultiplier ?? 1));
        return baseValue + (baseValue * (BasketTier - 1) * ShopStat("basket", x => x.ValuePerTier, 3) / 100);
    }

    public double GloveDamageReductionPercent
        => Math.Min(90d, Math.Max(0, GloveTier - 1) * ShopDoubleStat("glove", x => x.EffectPerTier, 3d));

    public string GloveDescription
        => $"TIER {GloveTier} • DMG -{GloveDamageReductionPercent:0.#}% • MAX HP {MoneyFormatter.Format(MaxHp)} • YOUR HAND SURVIVES MORE";
    public string BasketDescription => $"TIER {BasketTier} • HOLD {MoneyFormatter.Format(BagCapacity)} • SELL +{Math.Max(0, BasketTier - 1) * ShopStat("basket", x => x.ValuePerTier, 3)}% • MORE FLUFF, LESS STUFFING";

    public GameSaveState CaptureSaveState()
    {
        return new GameSaveState
        {
            Money = Money.ToString(CultureInfo.InvariantCulture),
            Hp = Hp,
            GloveTier = GloveTier,
            BasketTier = BasketTier,
            IsDead = IsDead,
            LastLostMoney = LastLostMoney.ToString(CultureInfo.InvariantCulture),
            NextMarketRestockUtcTicks = NextMarketRestockAt.Ticks,
            Inventory = new Dictionary<string, int>(inventory, StringComparer.Ordinal),
            MarketStock = new Dictionary<string, int>(MarketStock, StringComparer.Ordinal),
            FavoriteCottonIds = Favorited.ToList(),
            Stems = stems.Values.Select(stem => new GameSaveStem
            {
                X = stem.X,
                Y = stem.Y,
                State = stem.State,
                TypeId = stem.Type?.Id,
                MutationId = stem.Mutation?.Id,
                ReadyAtUtcTicks = stem.ReadyAt.Ticks,
                FertilizerId = stem.FertilizerIds.FirstOrDefault(),
                FertilizerIds = stem.FertilizerIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                RarityMultiplier = stem.RarityMultiplier,
                MutationMultiplier = stem.MutationMultiplier,
                MutationChanceBonus = stem.MutationChanceBonus
            }).ToList(),
            Basket = Basket.Select(ToSaveCotton).ToList(),
            FirstHarvestProtectionAvailable = FirstHarvestProtectionAvailable
        };
    }

    public bool RestoreSaveState(GameSaveState save)
    {
        if ((save.Version != 5 && save.Version != 6 && save.Version != 7 && save.Version != 8 && save.Version != 9) || !BigInteger.TryParse(save.Money, out var money) || money < 0 || save.GloveTier < 1 || save.BasketTier < 1)
            return false;

        stems.Clear();
        Basket.Clear();
        Favorited.Clear();
        inventory.Clear();

        Money = money;
        GloveTier = Math.Min(save.GloveTier, 1_000_000);
        BasketTier = Math.Min(save.BasketTier, 1_000_000);
        Hp = Math.Clamp(save.Hp, 0, MaxHp);
        IsDead = save.IsDead;
        LastLostMoney = BigInteger.TryParse(save.LastLostMoney, out var lost) && lost >= 0 ? lost : BigInteger.Zero;
        FirstHarvestProtectionAvailable = save.Version >= 6 && save.FirstHarvestProtectionAvailable;
        NextMarketRestockAt = new DateTime(save.NextMarketRestockUtcTicks > 0 ? save.NextMarketRestockUtcTicks : DateTime.UtcNow.AddSeconds(Market.RestockIntervalSeconds).Ticks, DateTimeKind.Utc);

        foreach (var pair in save.Inventory.Where(p => p.Value > 0 && marketItemsById.ContainsKey(p.Key)))
        {
            var restoredCount = pair.Value;
            // Saves from versions 5-7 may contain the old 4-seed starter bonus.
            // Strip only that legacy bonus once when migrating to version 8.
            if (save.Version < 8 && pair.Key.Equals("cotton_seed", StringComparison.OrdinalIgnoreCase))
                restoredCount = Math.Max(0, restoredCount - 4);
            var cap = pair.Key.Equals("cotton_seed", StringComparison.OrdinalIgnoreCase) ? SeedInventoryCap : 1_000_000;
            if (restoredCount > 0) inventory[pair.Key] = Math.Min(restoredCount, cap);
        }
        foreach (var item in Market.Items)
            MarketStock[item.Id] = item.Id.Equals("cotton_seed", StringComparison.OrdinalIgnoreCase) ? -1 : item.MaxStock;
        foreach (var pair in save.MarketStock.Where(p => p.Value >= 0 && marketItemsById.ContainsKey(p.Key)))
        {
            var item = marketItemsById[pair.Key];
            if (item.Id.Equals("cotton_seed", StringComparison.OrdinalIgnoreCase)) continue;
            MarketStock[pair.Key] = Math.Min(pair.Value, item.MaxStock);
        }

        foreach (var item in save.Stems.Take(100_000))
        {
            if (stems.ContainsKey((item.X, item.Y))) continue;
            var type = CottonTypes.FirstOrDefault(x => x.Id == item.TypeId);
            var mutation = Mutations.FirstOrDefault(x => x.Id == item.MutationId);
            var state = item.State == StemState.Ready && type == null ? StemState.Growing : item.State;
            var restoredStem = new CottonStem
            {
                X = item.X,
                Y = item.Y,
                State = state,
                Type = type,
                Mutation = mutation,
                ReadyAt = new DateTime(item.ReadyAtUtcTicks > 0 ? item.ReadyAtUtcTicks : DateTime.UtcNow.Ticks, DateTimeKind.Utc),
                RarityMultiplier = Math.Clamp(item.RarityMultiplier, 1, 1000),
                MutationMultiplier = Math.Clamp(item.MutationMultiplier, 1, 1000),
                MutationChanceBonus = Math.Clamp(item.MutationChanceBonus, 0, 0.8)
            };
            foreach (var fertilizerId in item.FertilizerIds.Where(id => marketItemsById.TryGetValue(id, out var f) && f.Kind.Equals("fertilizer", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase))
                restoredStem.FertilizerIds.Add(fertilizerId);
            if (restoredStem.FertilizerIds.Count == 0 && !string.IsNullOrWhiteSpace(item.FertilizerId) && marketItemsById.TryGetValue(item.FertilizerId, out var legacyFertilizer) && legacyFertilizer.Kind.Equals("fertilizer", StringComparison.OrdinalIgnoreCase))
                restoredStem.FertilizerIds.Add(legacyFertilizer.Id);
            if (restoredStem.FertilizerIds.Count > 0)
                RecalculateFertilizerEffects(restoredStem);
            stems[(item.X, item.Y)] = restoredStem;
        }

        foreach (var item in save.Basket.Take(100_000).Select(FromSaveCotton).Where(x => x != null))
            Basket.Add(item!);
        foreach (var id in save.FavoriteCottonIds.Take(100_000))
            if (Basket.Any(x => x.Id == id)) Favorited.Add(id);

        if (stems.Count == 0)
            PlantStem(0, 0, 3, 3, consumeSeed: false);
        return true;
    }

    static GameSaveCotton ToSaveCotton(CottonInstance item) => new() { Id = item.Id, TypeId = item.Type.Id, MutationId = item.Mutation?.Id };

    CottonInstance? FromSaveCotton(GameSaveCotton item)
    {
        var type = CottonTypes.FirstOrDefault(x => x.Id == item.TypeId);
        if (type == null) return null;
        return new CottonInstance(item.Id) { Type = type, Mutation = Mutations.FirstOrDefault(x => x.Id == item.MutationId) };
    }

    string TierAsset(string prefix, int tier, bool hold = false)
    {
        var visualTier = ((Math.Max(1, tier) - 1) % 20) + 1;
        var suffix = hold ? "_hold" : "";
        return $"assets/img/{prefix}_tier{visualTier}{suffix}.png";
    }

    public bool ToggleFavorite(string cottonId)
    {
        if (!Basket.Any(x => x.Id == cottonId)) return false;
        if (!Favorited.Add(cottonId))
            Favorited.Remove(cottonId);
        return true;
    }

    void Consume(string itemId)
    {
        var count = inventory.GetValueOrDefault(itemId);
        if (count <= 1) inventory.Remove(itemId);
        else inventory[itemId] = count - 1;
    }

    public record MarketSupplyView(MarketItemConfig Item, int Count, int Stock, BigInteger Cost);
    public record CottonInventoryView(string Id, string Name, string Image, int BaseValue, string? Mutation, string MutationImage, string DamageRange, double MutationDamageMultiplier, BigInteger Value, bool Favorited);
}
