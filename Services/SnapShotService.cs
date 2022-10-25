using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;

namespace Coflnet.Sky.ModCommands.Services
{
    public class SnapShotService
    {
        private Queue<SnapShot> _snapShots = new Queue<SnapShot>();

        public static SnapShotService Instance = new SnapShotService();

        public IEnumerable<SnapShot> SnapShots => _snapShots;
        public Prometheus.Gauge premUserCount = Prometheus.Metrics.CreateGauge("sky_mod_users", "How many premium users are connected");

        public void Take()
        {
            var otherUsers = FlipperService.Instance.Connections;
            premUserCount.Set(FlipperService.Instance.PremiumUserCount);
            var result = otherUsers.Where(c => c?.Connection != null).Select(c => new
            {
                c.ChannelCount,
                c.Connection.Settings?.Visibility,
                c.Connection.Settings?.ModSettings,
                c.Connection.Settings?.BasedOnLBin,
                c.Connection.Settings?.AllowedFinders,
                c.Connection.UserId
            });
            var state = JsonConvert.SerializeObject(result, Formatting.Indented);
            _snapShots.Enqueue(new SnapShot()
            {
                State = state,
                Time = DateTime.UtcNow
            });
        }

        public async Task Run(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    if (_snapShots.Count > 10)
                    {
                        _snapShots.Dequeue();
                    }
                    Take();
                }
                catch (System.Exception e)
                {
                    dev.Logger.Instance.Error(e, "taking snapshot");
                }
            }
        }

        public class SnapShot
        {
            public string State;

            public object Time { get; internal set; }
        }
    }
}
