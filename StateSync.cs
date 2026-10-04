using System;
using System.Threading.Tasks;
using SPT.Common.Http;
using UnityEngine;

namespace InfiniteEverything
{
    /// <summary>
    /// Infinite money and Infinite hideout resources need the server part (SPT_Runtime/user/mods/InfiniteEverything):
    /// the SPT server is what takes money and drains hideout fuel/filters. This sends the current state
    /// (POST /infiniteeverything/state {"money":bool,"hideout":bool}) whenever it changes and again every 60 s.
    /// The client-side overrides only act once the server has confirmed, so without the server part nothing changes.
    /// </summary>
    internal static class StateSync
    {
        internal const string Route = "/infiniteeverything/state";

        private static volatile bool _moneyConfirmed;
        private static volatile bool _hideoutConfirmed;
        private static volatile string _pendingNotice;
        private static string _sentState;
        private static float _nextResend;
        private static bool _warnedMissing;

        /// <summary>Infinite money is on AND the server part confirmed it.</summary>
        internal static bool MoneyActive => _moneyConfirmed && Plugin.On(Plugin.InfiniteMoney);

        /// <summary>Infinite hideout resources is on AND the server part confirmed it.</summary>
        internal static bool HideoutActive => _hideoutConfirmed && Plugin.On(Plugin.InfiniteHideoutResources);

        internal static void Tick()
        {
            string notice = _pendingNotice;
            if (notice != null)
            {
                _pendingNotice = null;
                Plugin.Notify(notice); // Unity UI: only from the main thread
            }

            bool money = Plugin.On(Plugin.InfiniteMoney);
            bool hideout = Plugin.On(Plugin.InfiniteHideoutResources);
            string body = "{\"money\":" + (money ? "true" : "false") + ",\"hideout\":" + (hideout ? "true" : "false") + "}";

            bool changed = body != _sentState;
            if (!changed && !((money || hideout) && Time.unscaledTime >= _nextResend))
            {
                return;
            }

            _sentState = body;
            _nextResend = Time.unscaledTime + 60f;
            if (!money)
            {
                _moneyConfirmed = false; // stop the client overrides at once
            }

            if (!hideout)
            {
                _hideoutConfirmed = false;
            }

            Send(body, money, hideout, changed);
        }

        private static void Send(string body, bool money, bool hideout, bool announce)
        {
            Task.Run(async () =>
            {
                try
                {
                    string reply = await RequestHandler.PostJsonAsync(Route, body);
                    bool moneyOk = money && reply != null && reply.Contains("\"money\":true");
                    bool hideoutOk = hideout && reply != null && reply.Contains("\"hideout\":true");
                    _moneyConfirmed = moneyOk;
                    _hideoutConfirmed = hideoutOk;
                    if (announce)
                    {
                        Plugin.Log.LogInfo($"Server state sent {body}, server replied {reply}");
                    }

                    if ((money && !moneyOk) || (hideout && !hideoutOk))
                    {
                        _pendingNotice = "Infinite Everything: the server part did not confirm - money/hideout options inactive";
                    }
                }
                catch (Exception ex)
                {
                    _moneyConfirmed = false;
                    _hideoutConfirmed = false;
                    if ((money || hideout) && !_warnedMissing)
                    {
                        _warnedMissing = true;
                        _pendingNotice = "Infinite money / hideout need the Infinite Everything server part (restart the server after installing)";
                    }

                    Plugin.Log.LogWarning($"{Route} failed ({ex.Message}). Is the server part installed and the server restarted?");
                }
            });
        }
    }
}
