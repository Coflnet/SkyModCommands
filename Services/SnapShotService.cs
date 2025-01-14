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

        private void Take()
        {
            var otherUsers = DiHandler.GetService<FlipperService>().Connections;
            var result = otherUsers.Where(c => c?.Connection != null).Select(c => {
                try
                {
                    
                    return new
                    {
                        c.ChannelCount,
                        c.Connection.Settings?.Visibility,
                        c.Connection.Settings?.ModSettings,
                        c.Connection.Settings?.BasedOnLBin,
                        c.Connection.Settings?.AllowedFinders,
                        c.Connection.UserId
                    };
                }
                catch(Core.CoflnetException)
                {
                    return null;
                }
                catch (Exception e)
                {
                    dev.Logger.Instance.Error(e, "taking snapshot");
                    return null;
                }
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
                catch (Exception e)
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
