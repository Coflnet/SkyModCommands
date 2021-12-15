
using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Coflnet.Sky.Base.Models
{
    [DataContract]
    public class Flip
    {
        [IgnoreDataMember]
        [JsonIgnore]
        public int Id { get; set; }
        [DataMember(Name = "auctionId")]
        public long AuctionId { get; set; }
        [DataMember(Name = "targetPrice")]
        public int TargetPrice { get; set; }
        [DataMember(Name = "finderType")]
        public LowPricedAuction.FinderType FinderType { get; set; }
        [System.ComponentModel.DataAnnotations.Timestamp]
        [DataMember(Name = "timestamp")]
        public DateTime Timestamp { get; set; }
    }
}