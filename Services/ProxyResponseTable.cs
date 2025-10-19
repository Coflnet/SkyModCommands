using Cassandra.Mapping.Attributes;
using System;

namespace Coflnet.Sky.ModCommands.Services;

[Table("proxy_responses")]
public class ProxyResponseTable
{
    [PartitionKey]
    [Column("id")]
    public string Id { get; set; }

    [Column("request_url")]
    public string RequestUrl { get; set; }

    [Column("response_body")]
    public string ResponseBody { get; set; }

    [Column("status_code")]
    public int StatusCode { get; set; }

    [Column("headers")]
    public string Headers { get; set; }

    [Column("user_id")]
    public string UserId { get; set; }

    [Column("locale")]
    public string Locale { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
