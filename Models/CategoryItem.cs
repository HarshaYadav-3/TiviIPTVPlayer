namespace TiviIPTVPlayer.Models
{
    public enum CategoryKind
    {
        All,
        Favorites,
        Group,
    }

    /// <summary>A category row in the left panel (All Channels / Favorites / a playlist group).</summary>
    public sealed class CategoryItem
    {
        public CategoryKind Kind { get; init; }

        /// <summary>Group name (only for Group kind).</summary>
        public string Name { get; init; } = string.Empty;

        public int Count { get; init; }

        public string Display => Kind switch
        {
            CategoryKind.All => "All Channels",
            CategoryKind.Favorites => "Favorites",
            _ => Name,
        };

        public override string ToString() => Display;
    }
}
