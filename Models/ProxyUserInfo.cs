using System;
using Cassandra.Mapping.Attributes;

namespace Coflnet.Sky.ModCommands.Models;

/// <summary>
/// Stores IP geolocation and quality information for proxy users
/// </summary>
[Table("proxy_user_info")]
public class ProxyUserInfo
{
    [PartitionKey]
    [Column("user_id")]
    public string UserId { get; set; }

    [Column("ip_address")]
    public string IpAddress { get; set; }

    [Column("country_code")]
    public string CountryCode { get; set; }

    [Column("latitude")]
    public double? Latitude { get; set; }

    [Column("longitude")]
    public double? Longitude { get; set; }

    [Column("city")]
    public string City { get; set; }

    [Column("region")]
    public string Region { get; set; }

    [Column("isp")]
    public string Isp { get; set; }

    [Column("is_vpn")]
    public bool IsVpn { get; set; }

    [Column("is_proxy")]
    public bool IsProxy { get; set; }

    [Column("fraud_score")]
    public int? FraudScore { get; set; }

    [Column("last_updated")]
    public DateTimeOffset LastUpdated { get; set; }

    [Column("ip_quality_raw")]
    public string IpQualityRaw { get; set; }

    [Column("ip_api_raw")]
    public string IpApiRaw { get; set; }
}
