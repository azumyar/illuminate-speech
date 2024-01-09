using System;

namespace Yarukizero.Net.IlluminateSpeech;

public class IlluminatePlugin : IDisposable {
	public virtual void Dispose() { }

	public virtual void Load(IIlluminateContext context) { }

	public virtual void StartPlugin() { }
	public virtual void EndPlugin() { }

	public virtual void DoBeforeSpeech(string text, bool isFinish) { }
	public virtual void Speak(string text, bool isFinish) { }
	public virtual void DoAfterSpeech(string text, bool isFinish) { }
}
