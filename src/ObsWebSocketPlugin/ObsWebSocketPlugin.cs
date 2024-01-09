using System;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;

namespace Yarukizero.Net.IlluminateSpeech.TextToSpeech.Obs {
	public class ObsWebSocketPlugin : IlluminatePlugin {


		public string Host { get; init; } = "127.0.0.1";
		public int Port { get; init; }

		private CancellationTokenSource? tokenSource;
		private ClientWebSocket? con;

		public override void StartPlugin() {
			this.tokenSource = new CancellationTokenSource();
			this.con = new ClientWebSocket();
			var _ = this.con.ConnectAsync(new Uri($"ws://{this.Host}:{this.Port}/ws/"), this.tokenSource.Token);
		}

		public override void EndPlugin() {
			this.con?.Dispose();
			this.con = null;
		}

		public override void Speech(string text, bool isFinish) {
			if(text.Trim().Any()) {

			}
		}
	}
}