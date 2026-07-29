using System;
using System.Threading.Tasks;
using Windows.Storage;

namespace MistMapper.GameBarWidget
{
    sealed class BridgeResponse
    {
        public bool IsOk { get; set; }
        public string Payload { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// File IPC with the desktop host via this package's LocalState folder.
    /// Host watches widget-request.txt and writes widget-response.txt / widget-state.json.
    /// </summary>
    static class IpcClient
    {
        const string StateFileName = "widget-state.json";
        const string HeartbeatFileName = "widget-heartbeat.txt";
        const string RequestFileName = "widget-request.txt";
        const string ResponseFileName = "widget-response.txt";

        public static async Task TouchHeartbeatAsync()
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.CreateFileAsync(HeartbeatFileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, DateTime.UtcNow.Ticks.ToString());
            }
            catch
            {
                // ignore — host falls back to GameBar process watch
            }
        }

        /// <summary>
        /// Remove the heartbeat so the host immediately drops Gamepad override
        /// when the widget is no longer visible.
        /// </summary>
        public static async Task ClearHeartbeatAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(HeartbeatFileName);
                await file.DeleteAsync();
            }
            catch
            {
                // already gone
            }
        }

        public static async Task<string> ReadStateAsync()
        {
            // Retry: host atomically replaces the file; a single failed read must not mean "offline".
            for (int attempt = 0; attempt < 4; ++attempt)
            {
                try
                {
                    var file = await ApplicationData.Current.LocalFolder.GetFileAsync(StateFileName);
                    var text = await FileIO.ReadTextAsync(file);
                    if (!string.IsNullOrWhiteSpace(text) && text.IndexOf('{') >= 0)
                        return text;
                }
                catch
                {
                    // retry
                }

                await Task.Delay(40 + attempt * 30);
            }

            return null;
        }

        public static async Task<BridgeResponse> SendAsync(string command, string payload = "")
        {
            var folder = ApplicationData.Current.LocalFolder;
            var requestId = Guid.NewGuid().ToString("N");
            var requestText = requestId + "\n" + command + "\n" + (payload ?? string.Empty) + "\n";

            var requestFile = await folder.CreateFileAsync(RequestFileName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(requestFile, requestText);

            for (int attempt = 0; attempt < 50; ++attempt)
            {
                await Task.Delay(100);
                try
                {
                    var responseFile = await folder.GetFileAsync(ResponseFileName);
                    var responseText = await FileIO.ReadTextAsync(responseFile);
                    var parts = responseText.Replace("\r", string.Empty).Split('\n');
                    if (parts.Length < 2 || !string.Equals(parts[0], requestId, StringComparison.Ordinal))
                        continue;

                    var status = parts[1];
                    var responsePayload = parts.Length >= 3 ? parts[2] : string.Empty;
                    return new BridgeResponse
                    {
                        IsOk = string.Equals(status, "OK", StringComparison.Ordinal),
                        Payload = responsePayload,
                        Error = string.Equals(status, "OK", StringComparison.Ordinal) ? null : responsePayload
                    };
                }
                catch
                {
                    // keep waiting
                }
            }

            return new BridgeResponse
            {
                IsOk = false,
                Error = "Timed out waiting for MistMapper host."
            };
        }
    }
}
