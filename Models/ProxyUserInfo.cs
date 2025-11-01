using System;
using Cassandra.Mapping.Attributes;

namespace Coflnet.Sky.ModCommands.Models;

/// <summary>
/// Stores essential IP geolocation information for proxy users
/// </summary>
[Table("proxy_user_info_2")]
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

    /// <summary>
    /// ASN type: "isp", "hosting", or "business"
    /// Prefer ISP types for proxy routing
    /// </summary>
    [Column("asn_type")]
    public string AsnType { get; set; }

    [Column("last_updated")]
    public DateTimeOffset LastUpdated { get; set; }
}
