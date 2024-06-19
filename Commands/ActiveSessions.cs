using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC;

[DataContract]
public class ActiveSessions
{
    [DataMember(Name = "sessions")]
    public List<ActiveSession> Sessions = new List<ActiveSession>();
    [DataMember(Name = "licenses")]
    public List<License> Licenses = new List<License>();
    [DataMember(Name = "useAccountTierOn")]
    public string UseAccountTierOn;
    [DataMember(Name = "userAccountTier")]
    public AccountTier UserAccountTier;
}

[DataContract]
public class License
{
    [DataMember(Name = "targetId")]
    public string TargetId = "";
    [DataMember(Name = "product")]
    public string Product = "";
    [DataMember(Name = "expiresAt")]
    public DateTime ExpiresAt;
}

[DataContract]
public class ActiveSession
{
    [DataMember(Name = "connectionId")]
    public string ConnectionId;
    [DataMember(Name = "lastActive")]
    public DateTime LastActive;
    [DataMember(Name = "connectedAt")]
    public DateTime ConnectedAt = DateTime.UtcNow;
    [DataMember(Name = "ip")]
    public string Ip;
    [DataMember(Name = "version")]
    public string Version;
    [DataMember(Name = "sessionId")]
    public string ClientSessionId;
    [DataMember(Name = "minecraftUuid")]
    public string MinecraftUuid;
    /// <summary>
    /// Service tier used for this connection
    /// </summary>
    [DataMember(Name = "tier")]
    public AccountTier Tier;
    [DataMember(Name = "outdated")]
    public bool Outdated;
}
