using Newtonsoft.Json;
using System;
using System.Threading;

namespace Yarukizero.Net.IlluminateSpeech;

public record class RecognitionObject {
#pragma warning disable CS8618
	[JsonProperty("transcript", Required = Required.Always)]
	public string Transcript { get; init; }
#pragma warning restore

	[JsonProperty("finish", Required = Required.Always)]
	public bool IsFinish { get; init; }
}

public interface IIlluminateRecognition {
	void InitPlugin();
	void DeinitPlugin();
	IObservable<RecognitionObject> Run(CancellationToken cancellationToken);
}

