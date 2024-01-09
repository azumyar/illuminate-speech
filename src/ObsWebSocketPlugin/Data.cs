using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yarukizero.Net.IlluminateSpeech.TextToSpeech.Obs {
	public class ObsMessage<T> where T:class {
		[JsonProperty("op")]
		public int Operation { get; private set; }

		[JsonProperty("d")]
		public T Data { get; private set; }

	}

	public class ObsReceivedData { }

	public class ObsReceivedData0 : ObsReceivedData {
		[JsonProperty("obsWebSocketVersion")]
		public string ObsWebSocketVersion { get; private set; }

		[JsonProperty("rpcVersion")]
		public int RpcVersion { get; private set; }


		[JsonProperty("authentication")]
		public ObsAuthentication? Authentication { get; private set; }
	}

	public class ObsReceivedData1 : ObsReceivedData {
		[JsonProperty("rpcVersion")]
		public int NegotiatedRpcVersion { get; private set; }

		[JsonProperty("authentication", NullValueHandling = NullValueHandling.Ignore)]
		public string? Authentication { get; private set; }

		[JsonProperty("eventSubscriptions")]
		public int EventSubscriptions { get; private set; }
	}

	public class ObsReceivedData2 : ObsReceivedData {
		[JsonProperty("negotiatedRpcVersion")]
		public int NegotiatedRpcVersion { get; private set; }
	}

	public class ObsReceivedData5 : ObsReceivedData {
		[JsonProperty("requestType")]
		public string RequestType { get; private set; }

		[JsonProperty("requestId")]
		public string RequestId { get; private set; }

		[JsonProperty("requestData")]
		public object RequestData { get; private set; }
	}

	public class ObsAuthentication {
		[JsonProperty("challenge")]
		public string Challenge { get; private set; }

		[JsonProperty("salt")]
		public string Salt { get; private set; }
	}
}
