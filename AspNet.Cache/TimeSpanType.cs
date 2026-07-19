namespace AspNet.Cache;

/// <summary>Unit used to interpret <see cref="CacheAttribute.ExpireAt"/>.</summary>
public enum TimeSpanType
{
    FromMinutes,
    FromMilliseconds,
    FromDays,
    FromHours,
    FromSeconds
}
