using log4net;
using System;
using System.IO;

namespace Yarukizero.Net.IlluminateSpeech;

public interface IIlluminateContext {
	public string PluginConfigDirectory => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
		"IlluminateSpeech",
		"plugins");

	public IIlluminateLog Logger { get; }
}
